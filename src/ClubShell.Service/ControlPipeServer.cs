using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Сервер канала для окна блокировки. Служба — единственный, кто знает конфиг и PIN:
/// окно спрашивает «можно ли выйти?», а служба проверяет PIN и решает.
/// </summary>
public sealed class ControlPipeServer : BackgroundService
{
    private readonly ShellConfig _config;
    private readonly LinkState _link;
    private readonly CommandBus _bus;
    private readonly ILogger<ControlPipeServer> _log;

    private int _pinFails;
    private DateTime _pinBlockedUntil = DateTime.MinValue;

    public ControlPipeServer(ShellConfig config, LinkState link, CommandBus bus, ILogger<ControlPipeServer> log)
        => (_config, _link, _bus, _log) = (config, link, bus, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stop);
                _ = HandleClientAsync(pipe, stop); // обслуживаем в фоне, а сами ждём следующего клиента
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Pipe accept failed");
                await Task.Delay(1000, stop);
            }
        }
    }

    /// <summary>Подключиться могут SYSTEM, администраторы и вошедшие пользователи (гость, под которым работает окно).</summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(PipeNames.Control, PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 0, 0, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        await using var _ = pipe;
        using var reader = new StreamReader(pipe);
        await using var writer = new StreamWriter(pipe) { AutoFlush = true };
        var gate = new SemaphoreSlim(1, 1); // писать в канал одновременно может и состояние, и ответ на PIN

        async Task Send(PipeMessage m)
        {
            await gate.WaitAsync(stop);
            try { await writer.WriteLineAsync(m.ToLine()); }
            finally { gate.Release(); }
        }

        Action<PipeMessage>? onCommand = null;
        try
        {
            _log.LogInformation("Locker connected");
            await Send(new PipeMessage { Type = "hello", PcName = _config.PcName, Zone = _config.Zone, RequireService = _config.RequireService,
                ServerUrl = _config.ServerUrl, PcToken = _config.PcToken });

            // команды сервера (завершить сеанс, сообщение) уходят окну, пока оно подключено
            onCommand = m => { Send(m).ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted); }; // ошибки записи (окно закрылось) не важны: канал сам закроется
            _bus.Published += onCommand;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var pusher = PushStateAsync(Send, cts.Token);

            while (await reader.ReadLineAsync(cts.Token) is { } line)
            {
                var msg = PipeMessage.FromLine(line);
                if (msg is null) continue;

                switch (msg.Type)
                {
                    case "status":
                        _link.SetStatus(msg.User, msg.SessionActive);
                        break;
                    case "adminPin":
                        await Send(new PipeMessage { Type = "adminResult", Ok = TryAdminPin(msg.Pin ?? "") });
                        break;
                }
            }
            cts.Cancel();
            await pusher;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        catch (Exception ex) { _log.LogError(ex, "Pipe client failed"); }
        finally { if (onCommand is not null) _bus.Published -= onCommand; _log.LogInformation("Locker disconnected"); }
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
