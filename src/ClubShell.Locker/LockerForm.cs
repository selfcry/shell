using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClubShell.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Locker;

/// <summary>
/// Окно блокировки. Два режима одного и того же окна:
///  • locked  — на весь экран поверх всего, хук клавиатуры включён;
///  • session — небольшая панель в углу с таймером, клавиши свободны.
/// Интерфейс (TypeScript) общается с окном через JSON-сообщения (см. ui/src/protocol.ts),
/// со службой — через named pipe (ServiceLink). Секретов (PIN, конфиг) у окна нет.
/// </summary>
internal sealed class LockerForm : Form
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Если служба молчит дольше, считаем, что её нет, и блокируем ПК.</summary>
    private static readonly TimeSpan ServiceSilenceLimit = TimeSpan.FromSeconds(15);

    private readonly bool _devMode = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL"));
    // Тестовый запуск без службы (CLUBSHELL_TEST=1): окно не требует службу, хук клавиатуры выключен, выход по PIN 0000.
    private readonly bool _test = Environment.GetEnvironmentVariable("CLUBSHELL_TEST") == "1";
    private readonly SwitchableClubApi _api = new(new FakeClubApi()); // заглушка, пока служба не пришлёт адрес сервера
    private readonly SessionManager _session;
    private readonly ServiceLink _service = new();
    private readonly KeyboardHook _hook = new();
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly System.Windows.Forms.Timer _guard = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _status = new() { Interval = 2000 };
    private TaskbarForm? _taskbar;
    private TaskManagerForm? _taskMgr;
    private Dictionary<string, ResolvedEntry> _catalog = new();

    private string? _serverUrl;
    private string _pcName = Environment.MachineName;
    private string _zone = "ARENA";
    private bool _requireService;

    private bool _serverOffline;      // сообщила служба: сервер не отвечает
    private string _serverReason = "Нет связи с сервером";
    private bool _linkOnline = true;  // итог, который показан гостю
    private string? _linkReason;
    private bool _allowExit;
    private bool _locked = true;

    public LockerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Controls.Add(_web);

        _requireService = !_test;
        _session = new SessionManager(_api, _pcName);
        _session.Tick += (left, balance) => Post(new { type = "tick", remainingSec = left, balance });
        _session.Ended += reason => RunOnUi(() => EndSession(reason));

        _guard.Tick += (_, _) => Reassert();
        _status.Tick += async (_, _) => { SystemTaskbar.Hide(); CheckService(); await SendStatusAsync(); };
        _service.MessageReceived += msg => RunOnUi(() => OnServiceMessage(msg));

        SystemTaskbar.Hide();
        SetMode(locked: true);
        Load += async (_, _) => await InitWebAsync();
        _service.Start();
        _status.Start();
    }

    // ---------- режимы окна ----------

    private void SetMode(bool locked)
    {
        _locked = locked;
        _hook.Enabled = locked && !_test;
        var screen = Screen.PrimaryScreen!;
        if (locked)
        {
            TopMost = true;
            Bounds = screen.Bounds;
            _guard.Start();
        }
        else
        {
            // Рабочий стол гостя: на весь экран, но не поверх всего — игры и приложения открываются над ним.
            _guard.Stop();
            TopMost = false;
            Bounds = screen.Bounds;
        }
        _taskbar?.SetVisible(!locked);
        if (locked) { _taskMgr?.HideManager(); if (IsHandleCreated) UnregisterHotKey(Handle, HotkeyId); }
        else if (IsHandleCreated) RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_ESCAPE); // Ctrl+Shift+Esc → наш диспетчер
        Post(new { type = "mode", mode = locked ? "locked" : "session" });
    }

    /// <summary>
    /// Сеанс окончен (время вышло, гость нажал «Завершить» или пропала связь): блокируем ПК и перезагружаем его.
    /// Файлы гостя удаляет служба при следующей загрузке, пока гость ещё не вошёл (см. ProfileCleaner).
    /// В тестовом режиме перезагрузки и очистки нет — только возврат к блокировке.
    /// </summary>
    private void EndSession(string reason)
    {
        Post(new { type = "sessionEnded", reason });
        Post(new { type = "rebooting", test = _test });
        SetMode(locked: true);
        if (_test) return;
        // Небольшая пауза, чтобы гость увидел экран «Перезагрузка».
        var delay = new System.Windows.Forms.Timer { Interval = 2500 };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            delay.Dispose();
            try { Process.Start(new ProcessStartInfo("shutdown", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false }); }
            catch (Exception ex) { Debug.WriteLine($"[ClubShell] reboot failed: {ex.Message}"); }
        };
        delay.Start();
    }

    /// <summary>Кнопка «Рабочий стол» на панели: выводим рабочий стол клуба вперёд.</summary>
    private void ShowDesktop()
    {
        if (_locked) return;
        Activate();
        _web.Focus();
    }

    /// <summary>Рабочий стол занимает экран за вычетом полосы панели задач.</summary>
    private void FitToWorkArea()
    {
        if (_locked) return;
        var b = Screen.PrimaryScreen!.Bounds;
        Bounds = new Rectangle(b.X, b.Y, b.Width, b.Height - (_taskbar?.ReservedHeight ?? 0));
    }

    /// <summary>В режиме блокировки не даём окну потерять фокус или спрятаться за другим окном.</summary>
    private void Reassert()
    {
        if (!_locked) return;
        TopMost = true;
        if (!ContainsFocus) { Activate(); _web.Focus(); }
    }

    // ---------- связь со службой ----------

    private void OnServiceMessage(PipeMessage msg)
    {
        switch (msg.Type)
        {
            case "hello":
                _pcName = msg.PcName ?? _pcName;
                _zone = msg.Zone ?? _zone;
                _requireService = msg.RequireService && !_devMode && !_test;
                _session.PcId = _pcName;
                if (!string.IsNullOrWhiteSpace(msg.ServerUrl) && !string.IsNullOrWhiteSpace(msg.PcToken) && _serverUrl != msg.ServerUrl)
                {
                    _serverUrl = msg.ServerUrl;
                    (_api.Inner as IDisposable)?.Dispose();
                    _api.Inner = new HttpClubApi(msg.ServerUrl, msg.PcToken); // дальше вход и тарифы идут на настоящий сервер
                }
                PostConfig();
                break;

            case "command":
                OnServerCommand(msg);
                break;

            case "state":
                _serverOffline = msg.Offline;
                _serverReason = msg.Reason ?? "Нет связи с сервером";
                UpdateLink();
                break;

            case "adminResult":
                Post(new { type = "adminExitResult", ok = msg.Ok });
                if (msg.Ok) { _allowExit = true; Application.Exit(); }
                break;
        }
    }

    /// <summary>Команды сервера (через службу): завершить сеанс, заблокировать, показать сообщение, поправить время.</summary>
    private void OnServerCommand(PipeMessage msg)
    {
        switch (msg.Name)
        {
            case "endSession" or "lock":
                if (_session.Active) _ = _session.StopAsync(msg.Text ?? "Сеанс завершён администратором");
                else if (!_locked) EndSession(msg.Text ?? "Сеанс завершён администратором");
                break;
            case "message":
                if (!string.IsNullOrWhiteSpace(msg.Text)) Post(new { type = "notice", text = msg.Text });
                break;
            case "sync":
                _session.SyncRemaining(msg.RemainingSec);
                break;
        }
    }

    /// <summary>Служба замолчала (убита или остановлена) — блокируем, пока она не вернётся.</summary>
    private void CheckService() => UpdateLink();

    private void UpdateLink()
    {
        var serviceSilent = _requireService && DateTime.UtcNow - _service.LastMessageUtc > ServiceSilenceLimit;
        var online = !_serverOffline && !serviceSilent;
        var why = _serverOffline ? _serverReason : "Служба не отвечает";

        if (online == _linkOnline && (online || why == _linkReason)) return;
        _linkOnline = online;
        _linkReason = online ? null : why;

        Post(new { type = "link", online, reason = _linkReason });
        if (!online && _session.Active) _ = _session.StopAsync(why); // сессия закрывается, ПК блокируется
    }

    private async Task SendStatusAsync() =>
        await _service.SendAsync(new PipeMessage { Type = "status", User = _session.User, SessionActive = _session.Active, RemainingSec = 0 });

    // ---------- WebView2 ----------

    private async Task InitWebAsync()
    {
        // Профиль браузера — в папке самого гостя (папка ProgramData закрыта для пользователей).
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClubShell", "webview2");
        var opts = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, userData, opts);
        await _web.EnsureCoreWebView2Async(env);

        // Панель задач — отдельное окно на том же профиле WebView2.
        _taskMgr = new TaskManagerForm(env);
        _ = _taskMgr.PrepareAsync(); // заранее, чтобы диспетчер открывался сразу
        _taskbar = new TaskbarForm(env, ShowDesktop, () => _taskMgr.Toggle());
        _ = _taskbar.PrepareAsync();
        _taskbar.Reserved += FitToWorkArea;
        _taskbar.SetVisible(!_locked);

        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false; // F5, Ctrl+R, F12 и т.п.
        s.IsPinchZoomEnabled = false;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL"); // например http://localhost:5173
        if (!string.IsNullOrEmpty(dev))
        {
            _web.CoreWebView2.Navigate(dev);
        }
        else
        {
            var uiDir = Path.Combine(AppContext.BaseDirectory, "ui");
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", uiDir, CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/index.html");
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    PostConfig();
                    Post(new { type = "link", online = _linkOnline, reason = _linkReason });
                    Post(new { type = "mode", mode = _locked ? "locked" : "session" });
                    LoadCatalog();
                    break;

                case "launch":
                    Launch(Str(root, "id"));
                    break;

                case "login":
                    await HandleLoginAsync(Str(root, "username"), Str(root, "password"));
                    break;

                case "registerStart":
                    await HandleRegisterStartAsync(Str(root, "username"), Str(root, "password"), Str(root, "birth"), Str(root, "phone"));
                    break;

                case "registerConfirm":
                    await HandleRegisterConfirmAsync(Str(root, "phone"), Str(root, "code"));
                    break;

                case "startSession":
                    await HandleStartAsync(Str(root, "packageId"));
                    break;

                case "stopSession":
                    await _session.StopAsync("Сессия завершена");
                    break;

                case "reboot":
                    if (!_session.Active) Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
                    break;

                case "adminExit":
                    if (_test)
                    {
                        var ok = Str(root, "pin") == "0000";
                        Post(new { type = "adminExitResult", ok });
                        if (ok) { _allowExit = true; Application.Exit(); }
                        break;
                    }
                    // PIN проверяет служба, окно его не знает; ответ придёт как adminResult
                    await _service.SendAsync(new PipeMessage { Type = "adminPin", Pin = Str(root, "pin") });
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubShell] bridge error: {ex}");
        }
    }

    private async Task HandleLoginAsync(string user, string password)
    {
        if (!_linkOnline)
        {
            Post(new { type = "loginResult", ok = false, error = _linkReason ?? "Нет связи с сервером" });
            return;
        }
        try
        {
            var r = await _api.LoginAsync(_pcName, user, password);
            if (r.Ok) _session.SetUser(r.User!, r.Balance);
            Post(new { type = "loginResult", ok = r.Ok, error = r.Error, user = r.User, balance = r.Balance, packages = r.Packages });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Post(new { type = "loginResult", ok = false, error = "Нет связи с сервером" });
        }
    }

    private async Task HandleRegisterStartAsync(string user, string password, string birth, string phone)
    {
        if (!_linkOnline)
        {
            Post(new { type = "registerCodeSent", ok = false, error = _linkReason ?? "Нет связи с сервером" });
            return;
        }
        try
        {
            var r = await _api.StartRegistrationAsync(_pcName, user, password, birth, phone);
            Post(new { type = "registerCodeSent", ok = r.Ok, error = r.Error, phone, resendSec = r.ResendSec, hint = r.Hint });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Post(new { type = "registerCodeSent", ok = false, error = "Нет связи с сервером" });
        }
    }

    private async Task HandleRegisterConfirmAsync(string phone, string code)
    {
        if (!_linkOnline)
        {
            Post(new { type = "loginResult", ok = false, error = _linkReason ?? "Нет связи с сервером" });
            return;
        }
        try
        {
            var r = await _api.ConfirmRegistrationAsync(_pcName, phone, code);
            if (r.Ok) _session.SetUser(r.User!, r.Balance);
            Post(new { type = "loginResult", ok = r.Ok, error = r.Error, user = r.User, balance = r.Balance, packages = r.Packages });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Post(new { type = "loginResult", ok = false, error = "Нет связи с сервером" });
        }
    }

    private async Task HandleStartAsync(string packageId)
    {
        if (_session.User is null) { Post(new { type = "startResult", ok = false, error = "Сначала войдите" }); return; }
        if (!_linkOnline) { Post(new { type = "startResult", ok = false, error = _linkReason ?? "Нет связи с сервером" }); return; }
        try
        {
            var r = await _api.StartSessionAsync(_pcName, _session.User, packageId);
            Post(new { type = "startResult", ok = r.Ok, error = r.Error });
            if (!r.Ok) return;
            _session.Begin(r.Minutes, r.Balance);
            SetMode(locked: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Post(new { type = "startResult", ok = false, error = "Нет связи с сервером" });
        }
    }

    // ---------- каталог игр ----------

    private static string CatalogPath() =>
        Environment.GetEnvironmentVariable("CLUBSHELL_CATALOG")
        ?? Path.Combine(AppContext.BaseDirectory, "ui", "config", "catalog.json");

    /// <summary>Читает каталог, оставляет найденное на этом ПК и отдаёт интерфейсу только то, что нужно для показа.</summary>
    private void LoadCatalog()
    {
        // Поиск программ по дискам может занять секунды — делаем в фоне, чтобы интерфейс не зависал.
        _ = Task.Run(() =>
        {
            try
            {
                ExeFinder.Reset(); // заново ищем программы: могли установить после прошлой загрузки
                var resolved = CatalogLoader.Resolve(CatalogLoader.Load(CatalogPath()));
                var items = resolved.Select(r => new
                {
                    id = r.Entry.Id,
                    title = r.Entry.Title,
                    kind = r.Entry.Kind,
                    group = r.Entry.Group,
                    cover = r.Entry.Cover,
                    clubAccount = r.Entry.ClubAccount ? true : (bool?)null,
                    minAge = r.Entry.MinAge,
                }).ToList();
                RunOnUi(() =>
                {
                    _catalog = resolved.ToDictionary(r => r.Entry.Id);
                    Post(new { type = "catalog", items });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ClubShell] catalog failed: {ex.Message}");
                Post(new { type = "catalog", items = Array.Empty<object>() });
            }
        });
    }

    /// <summary>Запуск строго по id из каталога: путь и аргументы берутся из файла хоста, а не из сообщения интерфейса.</summary>
    private void Launch(string id)
    {
        if (_locked || !_catalog.TryGetValue(id, out var entry))
        {
            Post(new { type = "launchResult", id, ok = false, error = "Приложение недоступно" });
            return;
        }
        // TODO: проверка minAge по дате рождения гостя, когда она придёт с сервера; выдача клубного аккаунта.
        try
        {
            var psi = new ProcessStartInfo(entry.Path)
            {
                UseShellExecute = true, // нужно для steam://, shell:AppsFolder и обычных exe
                Arguments = CatalogLoader.ExpandArgs(entry.Args, _session.User, _pcName), // {user}, {pc}
            };
            if (!string.IsNullOrEmpty(entry.WorkDir)) psi.WorkingDirectory = entry.WorkDir;
            else if (!CatalogLoader.IsUri(entry.Path) && Path.GetDirectoryName(entry.Path) is { Length: > 0 } dir) psi.WorkingDirectory = dir;

            Process.Start(psi);
            Post(new { type = "launchResult", id, ok = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubShell] launch {id} failed: {ex.Message}");
            Post(new { type = "launchResult", id, ok = false, error = "Не удалось запустить" });
        }
    }

    // ---------- вспомогательное ----------

    private void PostConfig() => Post(new { type = "config", pcName = _pcName, zone = _zone });

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

    private void Post(object message)
    {
        var json = JsonSerializer.Serialize(message, Json);
        RunOnUi(() => _web.CoreWebView2?.PostWebMessageAsJson(json));
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam == HotkeyId && !_locked) { _taskMgr?.Toggle(); return; }
        base.WndProc(ref m);
    }

    private const int HotkeyId = 0x4C53, WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000, VK_ESCAPE = 0x1B;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemTaskbar.Restore();
        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit) e.Cancel = true; // Alt+F4 и закрытие из диспетчера окон игнорируем
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { SystemTaskbar.Restore(); _taskMgr?.Dispose(); _hook.Dispose(); _session.Dispose(); _service.Dispose(); _guard.Dispose(); _status.Dispose(); _taskbar?.Dispose(); }
        base.Dispose(disposing);
    }
}
