using ClubShell.Core;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Locker;

/// <summary>
/// Свой Alt+Tab: по центру экрана плашка со значками открытых окон. Tab листает, отпускание Alt выбирает, Esc отменяет.
/// Страница ui/switcher.html создаётся заранее и только прячется, поэтому появляется мгновенно.
/// </summary>
internal sealed class SwitcherForm : Form
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly CoreWebView2Environment _env;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly WindowTracker _windows = new();
    private List<WindowInfo> _items = new();
    private int _selected;
    private bool _ready;

    public bool IsOpen => Visible;

    public SwitcherForm(CoreWebView2Environment env)
    {
        _env = env;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TopMost = true;
        Controls.Add(_web);
    }

    // не отбираем фокус и не прерываем нажатый Alt
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

    public async Task PrepareAsync()
    {
        if (_ready) return;
        _ready = true;
        if (!IsHandleCreated) CreateHandle();
        await _web.EnsureCoreWebView2Async(_env);
        var s = _web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = Debugger.IsAttached;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = DevFlags.Get("CLUBSHELL_DEV_URL");
        if (!string.IsNullOrEmpty(dev)) _web.CoreWebView2.Navigate(dev.TrimEnd('/') + "/switcher.html");
        else
        {
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/switcher.html");
        }
    }

    /// <summary>Первое нажатие Alt+Tab открывает плашку, следующие листают по кругу.</summary>
    public void Step(bool back)
    {
        if (!_ready || _web.CoreWebView2 is null) return;
        if (!Visible)
        {
            _items = _windows.Snapshot().ToList();
            if (_items.Count == 0) return;
            _selected = _items.Count > 1 && !back ? 1 : back ? _items.Count - 1 : 0; // первое окно — текущее, нажатие выбирает следующее
            var w = Math.Min(Screen.PrimaryScreen!.WorkingArea.Width - 80, 150 * Math.Max(_items.Count, 3) + 80);
            Size = new Size(w, 230);
            var area = Screen.PrimaryScreen!.WorkingArea;
            Location = new Point(area.Left + (area.Width - w) / 2, area.Top + (area.Height - 230) / 2);
            Post(new { type = "switcher", items = _items.Select(i => new { id = i.Id, title = i.Title, icon = i.Icon, exe = i.Exe }), selected = _selected });
            Show();
            return;
        }
        _selected = (_selected + (back ? -1 : 1) + _items.Count) % _items.Count;
        Post(new { type = "select", selected = _selected });
    }

    /// <summary>Alt отпущен: показываем выбранное окно.</summary>
    public void Commit()
    {
        if (!Visible) return;
        Hide();
        if (_selected >= 0 && _selected < _items.Count) _windows.Focus(_items[_selected].Id);
    }

    public void Cancel() => Hide();

    private void Post(object message) => _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
}
