using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Locker;

/// <summary>Диспетчер задач клуба: окно по центру экрана со страницей ui/taskmgr.html. Создаётся один раз и потом только прячется, поэтому открывается мгновенно.</summary>
internal sealed class TaskManagerForm : Form
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly CoreWebView2Environment _env;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly ProcessMonitor _monitor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };
    private bool _webReady;

    public TaskManagerForm(CoreWebView2Environment env)
    {
        _env = env;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TopMost = true;
        Size = new Size(780, 600);
        Controls.Add(_web);
        _timer.Tick += (_, _) => PostProcesses();
    }

    /// <summary>Подготовить окно заранее (в фоне, пока гость выбирает тариф), чтобы потом оно открывалось сразу.</summary>
    public async Task PrepareAsync()
    {
        if (_webReady) return;
        _webReady = true;
        if (!IsHandleCreated) CreateHandle();
        await _web.EnsureCoreWebView2Async(_env);
        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL");
        if (!string.IsNullOrEmpty(dev)) _web.CoreWebView2.Navigate(dev.TrimEnd('/') + "/taskmgr.html");
        else
        {
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/taskmgr.html");
        }
    }

    public async void Toggle()
    {
        if (Visible) { HideManager(); return; }
        await PrepareAsync();
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        Show();
        Activate();
        PostProcesses();
        _timer.Start();
    }

    public void HideManager()
    {
        _timer.Stop();
        Hide();
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready": PostProcesses(); break;
                case "closeTaskManager": HideManager(); break;
                case "endTask":
                    if (root.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out var id)) _monitor.End(id);
                    PostProcesses();
                    break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] taskmgr bridge error: {ex}"); }
    }

    private void PostProcesses()
    {
        if (_web.CoreWebView2 is null) return;
        var snap = _monitor.Snapshot();
        var items = snap.Items.Select(p => new { pid = p.Pid, name = p.Name, title = p.Title, cpu = p.Cpu, memMb = p.MemMb, app = p.App });
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(
            new { type = "processes", items, cpu = snap.Cpu, memUsedMb = snap.MemUsedMb, memTotalMb = snap.MemTotalMb }, Json));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideManager(); }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
