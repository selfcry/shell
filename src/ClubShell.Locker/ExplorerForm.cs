using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClubShell.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClubShell.Locker;

/// <summary>
/// «Мои файлы»: собственный проводник гостя (страница ui/explorer.html). Все операции идут через GuestFileSystem:
/// только папки профиля и внешние диски, без системных папок и ссылок. Системный Проводник Windows гостю не нужен.
/// </summary>
internal sealed class ExplorerForm : Form
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Скрипты и реестр открывать из проводника нельзя; исполняемые файлы разрешены, но запрещённые программы из списка сервера закрываются сами.
    private static readonly string[] DeniedExt = { ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".reg", ".msc", ".hta", ".scr", ".com", ".cpl", ".lnk" };

    private readonly CoreWebView2Environment _env;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    private readonly Func<string, bool> _isBlocked;
    private GuestFileSystem? _fs;
    private bool _ready;

    public ExplorerForm(CoreWebView2Environment env, Func<string, bool> isBlocked)
    {
        _env = env;
        _isBlocked = isBlocked;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TopMost = true;
        Size = new Size(1020, 660);
        Controls.Add(_web);
    }

    private static GuestFileSystem BuildFs()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<FsRoot>
        {
            new("Загрузки", Path.Combine(profile, "Downloads")),
            new("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new("Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            new("Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            new("Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        };
        // внешние диски (флешки), но не диск с системой
        var sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Removable && d.IsReady && !string.Equals(d.RootDirectory.FullName, sysRoot, StringComparison.OrdinalIgnoreCase))
                    roots.Add(new FsRoot($"Диск {d.Name.TrimEnd('\\')}", d.RootDirectory.FullName));
            }
            catch (Exception) { /* диск отключили */ }
        }
        return new GuestFileSystem(roots);
    }

    private async Task PrepareAsync()
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
        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

        var dev = Environment.GetEnvironmentVariable("CLUBSHELL_DEV_URL");
        if (!string.IsNullOrEmpty(dev)) _web.CoreWebView2.Navigate(dev.TrimEnd('/') + "/explorer.html");
        else
        {
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping("app.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate("https://app.local/explorer.html");
        }
    }

    public async void ShowExplorer()
    {
        _fs = BuildFs(); // заново: могли вставить флешку
        await PrepareAsync();
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        Show();
        Activate();
        if (_web.CoreWebView2 is not null) _web.CoreWebView2.Reload(); // чистое состояние при каждом открытии
    }

    public void HideExplorer() => Hide();

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "closeExplorer": HideExplorer(); break;
                case "fs": HandleFs(root); break;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] explorer bridge error: {ex}"); }
    }

    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";

    private void HandleFs(JsonElement r)
    {
        var id = r.GetProperty("id").GetInt32();
        var op = Str(r, "op");
        try
        {
            var fs = _fs ?? throw new FsException("Проводник не готов");
            switch (op)
            {
                case "roots":
                    Reply(new { type = "fsResult", id, ok = true, roots = fs.Roots.Select(x => new { name = x.Name, path = x.Path }) });
                    return;
                case "list":
                {
                    var path = Str(r, "path");
                    var entries = fs.List(path).Select(x => new { name = x.Name, isDir = x.IsDir, size = x.Size, modified = x.Modified });
                    Reply(new { type = "fsResult", id, ok = true, path = fs.Resolve(path), entries });
                    return;
                }
                case "open":
                {
                    var full = fs.Resolve(Str(r, "path"));
                    var ext = Path.GetExtension(full).ToLowerInvariant();
                    if (DeniedExt.Contains(ext)) throw new FsException("Этот тип файлов открывать запрещено");
                    if (_isBlocked(Path.GetFileName(full))) throw new FsException("Эта программа запрещена в клубе");
                    Process.Start(new ProcessStartInfo(full) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(full) ?? "" });
                    break;
                }
                case "mkdir": fs.CreateFolder(Str(r, "path"), Str(r, "name")); break;
                case "rename": fs.Rename(Str(r, "path"), Str(r, "name")); break;
                case "delete": fs.Delete(Str(r, "path")); break;
                case "copy": fs.Copy(Str(r, "path"), Str(r, "dest")); break;
                case "move": fs.Move(Str(r, "path"), Str(r, "dest")); break;
                default: throw new FsException("Неизвестная операция");
            }
            Reply(new { type = "fsResult", id, ok = true });
        }
        catch (FsException ex) { Reply(new { type = "fsResult", id, ok = false, error = ex.Message }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[ClubShell] explorer {op}: {ex.Message}");
            Reply(new { type = "fsResult", id, ok = false, error = "Не получилось: файл занят или нет доступа" });
        }
    }

    private void Reply(object message) => _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideExplorer(); }
        base.OnFormClosing(e);
    }
}
