using System.Diagnostics;

namespace ClubShell.Locker;

/// <summary>
/// Конец сеанса: закрываем всё, что работает в сессии гостя, кроме самой оболочки и системных процессов Windows.
/// Окно работает под гостем, поэтому чужие процессы (SYSTEM, службы) оно закрыть и не сможет — ошибки доступа пропускаем.
/// Вызывается только на боевом ПК: в тестовом режиме это закрыло бы программы разработчика.
/// </summary>
internal static class GuestProcesses
{
    /// <summary>Процессы сессии, без которых Windows или оболочка не работают.</summary>
    private static readonly HashSet<string> Keep = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClubShell.Locker", "msedgewebview2",                 // оболочка и её WebView2
        "csrss", "winlogon", "dwm", "fontdrvhost", "LogonUI", "userinit",
        "explorer", "sihost", "svchost", "taskhostw", "ctfmon", "RuntimeBroker", "dllhost", "conhost",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
        "ApplicationFrameHost", "SystemSettingsBroker", "smartscreen", "SecurityHealthSystray", "audiodg",
        "System", "Idle", "Registry", "smss", "wininit", "services", "lsass", "Memory Compression",
    };

    public static int KillAll()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var self = Environment.ProcessId;
        var killed = 0;
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || p.SessionId != session || Keep.Contains(p.ProcessName)) continue;
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
                catch (Exception) { /* уже завершился или нет доступа (системный процесс) */ }
            }
        }
        return killed;
    }
}
