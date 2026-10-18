using System.Runtime.InteropServices;

namespace ClubShell.Locker;

/// <summary>
/// Прячет системную панель задач Windows (если запущен explorer.exe), чтобы у гостя была только наша.
/// Когда оболочкой Windows служит сама ClubShell (Shell=Locker, explorer не запускается), панели нет и ничего делать не нужно.
/// Прежнее состояние панели возвращается при выходе из программы.
/// </summary>
internal static class SystemTaskbar
{
    private static bool _hidden;
    private static int _savedState = -1;

    // Состояние панели Windows до нас записывается в файл: если оболочку убили (диспетчер задач, сбой, STOP_ALL),
    // при следующем запуске или командой «ClubShell.Locker.exe --restore-taskbar» всё возвращается.
    private static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClubShell", "taskbar-state.txt");

    /// <summary>Вернуть панель Windows, если прошлый запуск не успел это сделать. Вызывается в начале работы.</summary>
    public static void RestoreLeftover()
    {
        if (File.Exists(StateFile)) ForceRestore();
    }

    /// <summary>Принудительно вернуть панель Windows (команда --restore-taskbar): прежнее состояние из файла, а если файла нет — обычное (без автоскрытия).</summary>
    public static void ForceRestore()
    {
        var state = 0;
        try { if (int.TryParse(File.ReadAllText(StateFile).Trim(), out var v)) state = v; } catch (Exception) { /* файла нет */ }
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = main, lParam = (IntPtr)state };
            SHAppBarMessage(ABM_SETSTATE, ref data);
        }
        foreach (var h in Taskbars()) ShowWindow(h, SW_SHOWNA);
        try { File.Delete(StateFile); } catch (Exception) { /* не страшно */ }
        _hidden = false;
    }

    /// <summary>Спрятать (и держать спрятанной: explorer может показать панель снова).</summary>
    public static void Hide()
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main == IntPtr.Zero) return; // explorer не запущен

        if (!_hidden)
        {
            // Автоскрытие освобождает у системы резерв места, иначе под нашей панелью осталась бы пустая полоса.
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = main };
            _savedState = (int)(long)SHAppBarMessage(ABM_GETSTATE, ref data);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
                File.WriteAllText(StateFile, _savedState.ToString());
            }
            catch (Exception) { /* без файла просто не будет аварийного восстановления */ }
            data.lParam = (IntPtr)(_savedState | ABS_AUTOHIDE);
            SHAppBarMessage(ABM_SETSTATE, ref data);
            _hidden = true;
        }
        foreach (var h in Taskbars()) if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    public static void Restore()
    {
        if (!_hidden) return;
        _hidden = false;
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero)
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = main, lParam = (IntPtr)Math.Max(_savedState, 0) };
            SHAppBarMessage(ABM_SETSTATE, ref data);
        }
        foreach (var h in Taskbars()) ShowWindow(h, SW_SHOWNA);
        try { File.Delete(StateFile); } catch (Exception) { /* не страшно */ }
    }

    private static IEnumerable<IntPtr> Taskbars()
    {
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero) yield return main;
        var h = IntPtr.Zero;
        while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
    }

    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1, SW_HIDE = 0, SW_SHOWNA = 8;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
}
