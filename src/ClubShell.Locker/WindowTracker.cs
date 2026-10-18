using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace ClubShell.Locker;

internal sealed record WindowInfo(string Id, string Title, string? Exe, string? Icon, bool Active, bool Minimized);

/// <summary>
/// Список окон для панели задач и управление ими. Окнами, которых нет в последнем списке,
/// управлять нельзя: идентификатор из интерфейса проверяется по списку, а не принимается на веру.
/// </summary>
internal sealed class WindowTracker
{
    private readonly Dictionary<string, string?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, IntPtr> _known = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public IReadOnlyList<WindowInfo> Snapshot()
    {
        var result = new List<WindowInfo>();
        var known = new Dictionary<long, IntPtr>();
        var fore = GetForegroundWindow();

        EnumWindows((h, _) =>
        {
            if (!IsTaskbarWindow(h, out var title)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (pid == _ownPid) return true; // оболочка и панель задач сами себя не показывают

            var exe = ExePath(pid);
            // служебные окна движка интерфейса самой оболочки (WebView2) гостю не нужны
            if (exe is not null && Path.GetFileName(exe).Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)) return true;
            known[h.ToInt64()] = h;
            result.Add(new WindowInfo(
                h.ToInt64().ToString(),
                title,
                exe is null ? null : Path.GetFileName(exe),
                IconOf(exe, h),
                h == fore,
                IsIconic(h)));
            return true;
        }, IntPtr.Zero);

        _known.Clear();
        foreach (var kv in known) _known[kv.Key] = kv.Value;
        return result;
    }

    /// <summary>Клик по кнопке: свёрнутое окно разворачивается, активное сворачивается, остальные выходят вперёд.</summary>
    public void Activate(string id)
    {
        if (!TryGet(id, out var h)) return;
        if (IsIconic(h)) { ShowWindow(h, SW_RESTORE); SetForegroundWindow(h); }
        else if (h == GetForegroundWindow()) ShowWindow(h, SW_MINIMIZE);
        else SetForegroundWindow(h);
    }

    /// <summary>Вывести окно вперёд (в отличие от Activate, активное окно не сворачивается): для переключателя Alt+Tab.</summary>
    public void Focus(string id)
    {
        if (!TryGet(id, out var h)) return;
        if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
        SetForegroundWindow(h);
    }

    public void Close(string id)
    {
        if (TryGet(id, out var h)) PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public void MinimizeAll()
    {
        foreach (var h in _known.Values.ToArray())
            if (!IsIconic(h)) ShowWindow(h, SW_MINIMIZE);
    }

    private bool TryGet(string id, out IntPtr h)
    {
        h = IntPtr.Zero;
        return long.TryParse(id, out var n) && _known.TryGetValue(n, out h) && IsWindow(h);
    }

    // ---------- фильтр окон ----------

    private static bool IsTaskbarWindow(IntPtr h, out string title)
    {
        title = "";
        if (!IsWindowVisible(h)) return false;

        var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        var hasOwner = GetWindow(h, GW_OWNER) != IntPtr.Zero;
        var toolWindow = (ex & WS_EX_TOOLWINDOW) != 0;
        var appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (toolWindow && !appWindow) return false;
        if (hasOwner && !appWindow) return false;

        // Окна универсальных приложений, спрятанные системой (cloaked), в списке не нужны.
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        var len = GetWindowTextLength(h);
        if (len == 0) return false;
        var sb = new StringBuilder(len + 1);
        GetWindowText(h, sb, sb.Capacity);
        title = sb.ToString();

        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        return cls.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static string? ExePath(uint pid)
    {
        var p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (p == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(p); }
    }

    /// <summary>
    /// Значок приложения как PNG в data URL. Сначала берётся из самого exe; если путь недоступен
    /// (процесс с повышенными правами) или это оболочка универсальных приложений — значок самого окна.
    /// </summary>
    private string? IconOf(string? exe, IntPtr hwnd)
    {
        var frameHost = exe is not null && Path.GetFileName(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
        var key = exe is null || frameHost ? "hwnd:" + hwnd.ToInt64() : exe;
        if (_iconCache.TryGetValue(key, out var cached) && cached is not null) return cached;

        string? url = null;
        try
        {
            if (exe is not null && !frameHost)
            {
                using var icon = Icon.ExtractAssociatedIcon(exe);
                url = icon is null ? null : ToDataUrl(icon);
            }
            if (url is null)
            {
                var hIcon = WindowIcon(hwnd);
                if (hIcon != IntPtr.Zero)
                {
                    using var icon = Icon.FromHandle(hIcon); // чужой дескриптор не уничтожаем
                    url = ToDataUrl(icon);
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] icon {exe}: {ex.Message}"); }
        _iconCache[key] = url;
        return url;
    }

    /// <summary>Значок из exe по пути (для закреплённых программ). null — путь не exe или значок не получился.</summary>
    public static string? IconFromPath(string path)
    {
        try
        {
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : ToDataUrl(icon);
        }
        catch (Exception) { return null; }
    }

    private static string ToDataUrl(Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    private static IntPtr WindowIcon(IntPtr h)
    {
        foreach (var kind in new[] { ICON_BIG, ICON_SMALL2, ICON_SMALL })
            if (SendMessageTimeout(h, WM_GETICON, (IntPtr)kind, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out var r) != IntPtr.Zero && r != IntPtr.Zero)
                return r;
        return GetClassLongPtr(h, GCLP_HICON);
    }

    // ---------- WinAPI ----------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
    private const uint GW_OWNER = 4, WM_CLOSE = 0x10, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int SW_MINIMIZE = 6, SW_RESTORE = 9, DWMWA_CLOAKED = 14;

    private const uint WM_GETICON = 0x7F, SMTO_ABORTIFHUNG = 2;
    private const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2, GCLP_HICON = -14;

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] private static extern IntPtr GetClassLongPtr(IntPtr h, int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr p, int flags, StringBuilder s, ref int size);
}
