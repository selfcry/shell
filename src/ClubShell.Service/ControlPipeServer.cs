using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Сервер канала для окна блокировки. Служба — единственный, кто знает конфиг, PIN и токен ПК:
/// окно спрашивает «можно ли выйти?» и просит войти/начать сеанс, а служба проверяет и сама обращается к серверу клуба.
/// Статус, PIN и запросы принимаются только от процесса окна, которое запустил страж (LockerTracker):
/// другой процесс гостя может подключиться к каналу, но ничего не добьётся.
/// </summary>
public sealed class ControlPipeServer : BackgroundService
{
    /// <summary>Окно одно; запас на переподключение, пока старый экземпляр канала ещё закрывается.</summary>
    private const int MaxInstances = 4;
    /// <summary>Одновременных запросов к серверу от одного клиента (вход, каталог, тест скорости…).</summary>
    private const int MaxRequestsPerClient = 4;

    private readonly ShellConfig _config;
    private readonly LinkState _link;
    private readonly CommandBus _bus;
    private readonly IClubApi _api;
    private readonly LockerTracker _tracker;
    private readonly ILogger<ControlPipeServer> _log;

    private readonly object _pinGate = new();
    private int _pinFails;
    private DateTime _pinBlockedUntil = DateTime.MinValue;
    private NamedPipeServerStream? _firstPipe;

    public ControlPipeServer(ShellConfig config, LinkState link, CommandBus bus, IClubApi api, LockerTracker tracker, ILogger<ControlPipeServer> log)
        => (_config, _link, _bus, _api, _tracker, _log) = (config, link, bus, api, tracker, log);

    /// <summary>
    /// Первый экземпляр канала создаётся синхронно при старте службы, до чистки профиля и стража,
    /// с FirstPipeInstance: если имя уже занято (процесс гостя успел создать канал-подделку), это сразу видно в журнале.
    /// </summary>
    public override Task StartAsync(CancellationToken ct)
    {
        try { _firstPipe = CreatePipe(first: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogCritical(ex, "Pipe {Name} is already taken by another process: the locker will refuse it (wrong session/path)", PipeNames.Control);
        }
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var pipe = Interlocked.Exchange(ref _firstPipe, null) ?? CreatePipe(first: false);
                try { await pipe.WaitForConnectionAsync(stop); }
                catch { await pipe.DisposeAsync(); throw; }
                _ = HandleClientAsync(pipe, stop); // обслуживаем в фоне, а сами ждём следующего клиента
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // все экземпляры заняты (кто-то держит подключения) или имя занято — пробуем позже
                _log.LogWarning("Pipe accept failed: {Message}", ex.Message);
                try { await Task.Delay(1000, stop); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Подключиться могут SYSTEM, администраторы и вошедшие пользователи (гость, под которым работает окно).</summary>
    private static NamedPipeServerStream CreatePipe(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : 0);
        return NamedPipeServerStreamAcl.Create(PipeNames.Control, PipeDirection.InOut,
            MaxInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        await using var owned = pipe;
        var clientPid = GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) ? (int)pid : 0;
        var reader = new PipeLineReader(pipe, PipeNames.MaxClientLine);
        await using var writer = new StreamWriter(pipe) { AutoFlush = true };
        var gate = new SemaphoreSlim(1, 1); // писать в канал одновременно могут состояние, ответы и команды
        var requests = new SemaphoreSlim(MaxRequestsPerClient, MaxRequestsPerClient);
        var warned = false;

        async Task Send(PipeMessage m)
        {
            await gate.WaitAsync(stop);
            try { await writer.WriteLineAsync(m.ToLine()); }
            finally { gate.Release(); }
        }

        // PID проверяется на каждом сообщении: окно могли перезапустить, пока канал был открыт
        bool Trusted()
        {
            if (_tracker.IsTracked(clientPid)) return true;
            if (!warned) { warned = true; _log.LogWarning("Pipe client pid {Pid} is not the locker started by the guardian (pid {Locker}), ignoring it", clientPid, _tracker.Pid); }
            return false;
        }

        Action<PipeMessage>? onCommand = null;
        try
        {
            _log.LogInformation("Pipe client connected, pid {Pid}", clientPid);
            await Send(new PipeMessage { Type = "hello", PcName = _config.PcName, Zone = _config.Zone, RequireService = _config.RequireService,
                ServerUrl = _config.ServerUrl });

            // команды сервера (завершить сеанс, сообщение) уходят окну, пока оно подключено
            onCommand = m => { if (_tracker.IsTracked(clientPid)) Send(m).ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); }; // ошибки записи (окно закрылось) не важны: канал сам закроется
            _bus.Published += onCommand;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var pusher = PushStateAsync(Send, cts.Token);

            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                var msg = PipeMessage.FromLine(line);
                if (msg is null || !Trusted()) continue;

                switch (msg.Type)
                {
                    case "status":
                        _link.SetLockerStatus(msg.Locked, msg.SessionActive);
                        break;
                    case "adminPin":
                        await Send(new PipeMessage { Type = "adminResult", Ok = TryAdminPin(msg.Pin ?? "") });
                        break;
                    case "request":
                        if (!await requests.WaitAsync(0, cts.Token))
                        {
                            await Send(new PipeMessage { Type = "response", Id = msg.Id, Error = "Слишком много запросов" });
                            break;
                        }
                        _ = Task.Run(async () =>
                        {
                            try { await Send(await HandleRequestAsync(msg)); }
                            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                            finally { requests.Release(); }
                        });
                        break;
                }
            }
            cts.Cancel();
            await pusher;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        catch (InvalidDataException ex) { _log.LogWarning("Pipe client pid {Pid} dropped: {Message}", clientPid, ex.Message); }
        catch (Exception ex) { _log.LogError(ex, "Pipe client failed"); }
        finally { if (onCommand is not null) _bus.Published -= onCommand; _log.LogInformation("Pipe client disconnected, pid {Pid}", clientPid); }
    }

    /// <summary>
    /// Запрос окна к серверу клуба. Гость, от имени которого начинается и завершается сеанс, — тот, кто вошёл через службу
    /// (ответ сервера на login/register), а не имя из запроса. fail=true — сервер недоступен.
    /// </summary>
    private async Task<PipeMessage> HandleRequestAsync(PipeMessage req)
    {
        var resp = new PipeMessage { Type = "response", Id = req.Id };
        try
        {
            switch (req.Op)
            {
                case "login":
                case "register":
                {
                    var user = req.User ?? "";
                    var r = req.Op == "login"
                        ? await _api.LoginAsync(_config.PcName, user, req.Password ?? "")
                        : await _api.RegisterAsync(_config.PcName, user, req.Password ?? "", req.Birth ?? "");
                    if (r.Ok && _link.ServerSessionActive != true) _link.SetUser(r.User);
                    (resp.Ok, resp.Error, resp.User, resp.Balance, resp.Packages, resp.Bonus, resp.Loyalty) = (r.Ok, r.Error, r.User, r.Balance, r.Packages?.ToList(), r.Bonus, r.Loyalty);
                    break;
                }
                case "logout":
                    if (_link.ServerSessionActive != true) _link.SetUser(null);
                    resp.Ok = true;
                    break;
                case "start":
                {
                    if (_link.User is not { } user) { resp.Error = "Сначала войдите"; break; }
                    var r = await _api.StartSessionAsync(_config.PcName, user, req.PackageId ?? "");
                    if (r.Ok) _link.SetServerSession(true); // не ждём пульса, иначе контроль сеанса мог бы счесть разблокировку нарушением
                    (resp.Ok, resp.Error, resp.Balance, resp.Minutes, resp.Bonus) = (r.Ok, r.Error, r.Balance, r.Minutes, r.Bonus);
                    break;
                }
                case "stop":
                {
                    var user = _link.User;
                    _link.SetServerSession(false);
                    if (user is not null) await _api.StopSessionAsync(_config.PcName, user);
                    _link.SetUser(null);
                    resp.Ok = true;
                    break;
                }
                case "call":
                    resp.Ok = await _api.CallAdminAsync(_link.User);
                    break;
                case "promo":
                {
                    // промокод — тому, кто вошёл через службу, а не имени из запроса
                    if (_link.User is not { } user) { resp.Error = "Сначала войдите"; break; }
                    var r = await _api.RedeemPromoAsync(user, req.Code ?? "");
                    (resp.Ok, resp.Error, resp.Bonus) = (r.Ok, r.Error, r.Bonus);
                    break;
                }
                case "catalog":
                    resp.Catalog = await _api.GetCatalogAsync();
                    resp.Ok = true;
                    break;
                case "speedtest":
                {
                    var hasServer = !string.IsNullOrWhiteSpace(_config.ServerUrl) && !string.IsNullOrWhiteSpace(_config.PcToken);
                    resp.Speed = await SpeedTest.RunAsync(hasServer ? _config.ServerUrl : null, hasServer ? _config.PcToken : null, SpeedTest.PingHost(_config.ServerUrl));
                    resp.Ok = resp.Speed.Ok;
                    break;
                }
                default:
                    resp.Error = "Неизвестный запрос";
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            (resp.Ok, resp.Fail, resp.Error) = (false, true, "Нет связи с сервером");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pipe request {Op} failed", req.Op);
            (resp.Ok, resp.Error) = (false, "Ошибка службы");
        }
        return resp;
    }

    /// <summary>Каждые 2 секунды сообщаем окну, есть ли связь. По этим сообщениям окно ещё и понимает, что служба жива.</summary>
    private async Task PushStateAsync(Func<PipeMessage, Task> send, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do { await send(new PipeMessage { Type = "state", Offline = _link.Offline, Reason = _link.Reason }); }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    /// <summary>5 ошибок подряд — минута без попыток. Успех создаёт maintenance: служба перестаёт перезапускать окно.</summary>
    private bool TryAdminPin(string pin)
    {
        lock (_pinGate)
        {
            if (DateTime.UtcNow < _pinBlockedUntil) return false;

            if (_config.VerifyPin(pin))
            {
                _pinFails = 0;
                File.WriteAllText(ShellConfig.MaintenanceFlag, DateTime.Now.ToString("O"));
                _log.LogWarning("Admin PIN accepted, maintenance mode on");
                return true;
            }

            if (++_pinFails >= 5)
            {
                _pinFails = 0;
                _pinBlockedUntil = DateTime.UtcNow.AddMinutes(1);
                _log.LogWarning("Too many wrong PIN attempts, blocked for 1 minute");
            }
            return false;
        }
    }
}
