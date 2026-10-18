using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Locker;

/// <summary>
/// Панель задач гостя: отдельное окно, закреплённое у нижнего края экрана как AppBar.
/// Система резервирует под него место (развёрнутые окна его не перекрывают), а когда поверх
/// открывается полноэкранная игра, приходит ABN_FULLSCREENAPP и панель уходит на задний план.
/// Показывает открытые окна, звук, сеть и часы; страница — ui/taskbar.html.
/// </summary>
internal sealed class TaskbarForm : Form
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int BaseHeight = 56; // высота при 96 DPI
    private readonly CoreWebView2Environment _env;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly WindowTracker _windows = new();
    private readonly AudioControl _audio = new();
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 700 };
    private readonly System.Windows.Forms.Timer _system = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _pingTimer = new() { Interval = 4000 };
    private readonly uint _callbackMsg = RegisterWindowMessage("ClubShell.AppBar");
    private readonly Action _showDesktop;
    private readonly Action _openTaskManager;
    private readonly Action<string> _launch;
    private string _pinnedJson = "";

    private bool _registered;
    private bool _fullscreenApp;
    private bool _webReady;
    private string _lastWindowsJson = "";

    /// <summary>Сколько пикселей экрана занимает панель (0, пока она скрыта).</summary>
    public int ReservedHeight => _registered ? Height : 0;

    /// <summary>Рабочая область изменилась (панель появилась, исчезла или сдвинулась).</summary>
    public event Action? Reserved;

    public TaskbarForm(CoreWebView2Environment env, Action showDesktop, Action openTaskManager, Action<string> launch)
    {
        _env = env;
        _launch = launch;
        _openTaskManager = openTaskManager;
        _showDesktop = showDesktop;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TopMost = true;
        Controls.Add(_web);

        _poll.Tick += (_, _) => PostWindows();
        _system.Tick += (_, _) => { PostAudio(); PostNetwork(); };
        _pingTimer.Tick += async (_, _) => { if (!NetworkTest.IsRunning) await PostPingAsync(); };
    }

    // не отбирает фокус у игры и не показывается в Alt+Tab
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /* WS_EX_TOOLWINDOW */ | 0x08000000 /* WS_EX_NOACTIVATE */;
            return cp;
        }
    }

    // ---------- показ и AppBar ----------

    /// <summary>Режим session: показать панель и занять место. Режим locked: убрать.</summary>
    public async void SetVisible(bool visible)
    {
        if (visible)
        {
            if (!IsHandleCreated) CreateHandle();
            if (!_webReady) await InitWebAsync();
            Show();
            RegisterBar();
            _poll.Start();
            _system.Start();
            _pingTimer.Start();
        }
        else
        {
            _poll.Stop();
            _system.Stop();
            _pingTimer.Stop();
            UnregisterBar();
            Hide();
        }
    }

    private int BarHeight => (int)Math.Round(BaseHeight * DeviceDpi / 96.0);

    private void RegisterBar()
    {
        if (_registered) { Position(); return; }
        var data = NewData();
        data.uCallbackMessage = _callbackMsg;
        if (SHAppBarMessage(ABM_NEW, ref data) == 0) return; // другой AppBar уже занят — работаем как обычное окно
        _registered = true;
        _fullscreenApp = false;
        Position();
    }

    private void UnregisterBar()
    {
        if (!_registered) return;
        var data = NewData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        _registered = false;
        Reserved?.Invoke();
    }

    /// <summary>Просим у системы нижнюю полосу экрана и занимаем её.</summary>
    private void Position()
    {
        var screen = Screen.PrimaryScreen!.Bounds;
        var h = BarHeight;
        var data = NewData();
        data.uEdge = ABE_BOTTOM;
        data.rc = new RECT { left = screen.Left, right = screen.Right, top = screen.Bottom - h, bottom = screen.Bottom };
        SHAppBarMessage(ABM_QUERYPOS, ref data);
        data.rc.top = data.rc.bottom - h; // система могла сдвинуть нижний край, высоту оставляем нашу
        SHAppBarMessage(ABM_SETPOS, ref data);
        Bounds = Rectangle.FromLTRB(data.rc.left, data.rc.top, data.rc.right, data.rc.bottom);
        Reserved?.Invoke();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == _callbackMsg && _registered)
        {
            switch ((int)m.WParam)
            {
                case ABN_FULLSCREENAPP:
                    // lParam != 0: поверх открылось полноэкранное окно — прячемся за него; закрылось — возвращаемся.
                    _fullscreenApp = m.LParam != IntPtr.Zero;
                    SetWindowPos(Handle, _fullscreenApp ? HWND_BOTTOM : HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    break;
                case ABN_POSCHANGED:
                    Position();
                    break;
            }
            return;
        }
        base.WndProc(ref m);
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = Handle };

    // ---------- WebView2 ----------

    /// <summary>Загрузить страницу панели заранее, пока гость выбирает тариф: к началу сеанса она уже готова.</summary>
    public async Task PrepareAsync()
    {
        if (!IsHandleCreated) CreateHandle();
        if (!_webReady) await InitWebAsync();
    }

    private async Task InitWebAsync()
    {
        _webReady = true;
        await _web.EnsureCoreWebView2Async(_env);
        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsPinchZoomEnabled = false;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL");
        if (!string.IsNullOrEmpty(dev))
            _web.CoreWebView2.Navigate(dev.TrimEnd('/') + "/taskbar.html");
        else
        {
            var uiDir = Path.Combine(AppContext.BaseDirectory, "ui");
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", uiDir, CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/taskbar.html");
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _lastWindowsJson = "";
                    if (_pinnedJson.Length > 0) PostRaw(_pinnedJson);
                    PostWindows();
                    PostAudio();
                    PostNetwork();
                    _ = PostPingAsync();
                    break;

                case "activateWindow":
                    _windows.Activate(Str(root, "id"));
                    PostWindows();
                    break;

                case "closeWindow":
                    _windows.Close(Str(root, "id"));
                    break;

                case "showDesktop":
                    _windows.MinimizeAll();
                    _showDesktop();
                    PostWindows();
                    break;

                case "openTaskManager":
                    _openTaskManager();
                    break;

                case "launch":
                    _launch(Str(root, "id")); // закреплённая программа: запускает оболочка по коду из каталога
                    break;

                case "speedTest":
                    _ = RunSpeedTestAsync();
                    break;

                case "setVolume":
                    if (root.TryGetProperty("value", out var vol) && vol.TryGetInt32(out var v)) _audio.SetVolume(v);
                    PostAudio();
                    break;

                case "setMute":
                    _audio.SetMute(root.TryGetProperty("muted", out var m) && m.GetBoolean());
                    PostAudio();
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] taskbar bridge error: {ex}"); }
    }

    /// <summary>Закреплённые программы из каталога (то, что админ отметил «на панели»).</summary>
    public void SetPinned(IEnumerable<(string id, string title, string? icon)> items)
    {
        _pinnedJson = JsonSerializer.Serialize(new { type = "pinned", items = items.Select(i => new { id = i.id, title = i.title, icon = i.icon }) }, Json);
        if (_web.CoreWebView2 is not null) PostRaw(_pinnedJson);
    }

    private void PostWindows()
    {
        var items = _windows.Snapshot().Select(w => new
        {
            id = w.Id, title = w.Title, exe = w.Exe, icon = w.Icon, active = w.Active, minimized = w.Minimized,
        }).ToList();
        var json = JsonSerializer.Serialize(new { type = "windows", items }, Json);
        if (json == _lastWindowsJson) return; // шлём только изменения
        _lastWindowsJson = json;
        PostRaw(json);
    }

    private void PostAudio()
    {
        try
        {
            var (volume, muted) = _audio.Get();
            Post(new { type = "audio", volume, muted });
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] audio failed: {ex.Message}"); }
    }

    private async Task PostPingAsync()
    {
        var ms = await NetworkTest.PingAsync();
        if (!IsDisposed) Post(new { type = "ping", ms });
    }

    private async Task RunSpeedTestAsync()
    {
        var r = await NetworkTest.RunAsync();
        if (!IsDisposed) Post(new { type = "speedResult", ok = r.Ok, downMbps = r.DownMbps, upMbps = r.UpMbps, pingMs = r.PingMs, error = r.Error, source = r.Source });
    }

    private void PostNetwork()
    {
        var (online, name, speed) = NetworkStatus.Read();
        Post(new { type = "network", online, name, speedMbps = speed });
    }

    private void Post(object message) => PostRaw(JsonSerializer.Serialize(message, Json));
    private void PostRaw(string json) => _web.CoreWebView2?.PostWebMessageAsJson(json);

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { UnregisterBar(); _poll.Dispose(); _system.Dispose(); _pingTimer.Dispose(); _audio.Dispose(); }
        base.Dispose(disposing);
    }

    // ---------- WinAPI ----------

    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    private const int ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2;
    private const int ABE_BOTTOM = 3;
    private static readonly IntPtr HWND_BOTTOM = new(1), HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
