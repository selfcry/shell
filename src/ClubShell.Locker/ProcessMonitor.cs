using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClubShell.Locker;

internal sealed record ProcItem(int Pid, string Name, string? Title, double Cpu, double MemMb, bool App);
internal sealed record ProcSnapshot(IReadOnlyList<ProcItem> Items, double Cpu, double MemUsedMb, double MemTotalMb);

/// <summary>
/// Процессы гостя для диспетчера задач: только текущая сессия Windows, без системных и без самой оболочки.
/// «Снять задачу» работает только для процессов из последнего списка.
/// </summary>
internal sealed class ProcessMonitor
{
    // Системные процессы и сама оболочка: не показываются и не завершаются.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "fontdrvhost",
        "sihost", "ctfmon", "explorer", "runtimebroker", "taskhostw", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "textinputhost", "conhost", "audiodg", "securityhealthsystray", "msedgewebview2", // webview2 — движок интерфейса оболочки
        "ClubShell.Locker", "ClubShell.Service",
    };

    private readonly Dictionary<int, (TimeSpan cpu, DateTime at)> _prev = new();
    private readonly HashSet<int> _known = new();
    private readonly int _session = Process.GetCurrentProcess().SessionId;

    public ProcSnapshot Snapshot()
    {
        var now = DateTime.UtcNow;
        var cores = Environment.ProcessorCount;
        var items = new List<ProcItem>();
        var seen = new HashSet<int>();
        double totalCpu = 0;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                using (p)
                {
                    if (p.SessionId != _session || Protected.Contains(p.ProcessName) || p.Id <= 4) continue;
                    seen.Add(p.Id);

                    double cpu = 0;
                    var total = p.TotalProcessorTime;
                    if (_prev.TryGetValue(p.Id, out var prev) && now > prev.at)
                        cpu = Math.Clamp((total - prev.cpu).TotalSeconds / (now - prev.at).TotalSeconds / cores * 100, 0, 100);
                    _prev[p.Id] = (total, now);
                    totalCpu += cpu;

                    var title = p.MainWindowTitle;
                    items.Add(new ProcItem(p.Id, p.ProcessName + ".exe", string.IsNullOrWhiteSpace(title) ? null : title,
                        Math.Round(cpu), Math.Round(p.WorkingSet64 / 1_048_576.0), p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(title)));
                }
            }
            catch (Exception) { /* процесс завершился или недоступен */ }
        }

        foreach (var gone in _prev.Keys.Where(k => !seen.Contains(k)).ToList()) _prev.Remove(gone);
        _known.Clear();
        foreach (var i in items) _known.Add(i.Pid);

        var (used, total2) = Memory();
        return new ProcSnapshot(items, Math.Min(100, totalCpu), used, total2);
    }

    /// <summary>Завершает процесс с деревом потомков, если он есть в последнем списке.</summary>
    public bool End(int pid)
    {
        if (!_known.Contains(pid)) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.SessionId != _session || Protected.Contains(p.ProcessName)) return false;
            p.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubShell] end task {pid}: {ex.Message}");
            return false;
        }
    }

    private static (double usedMb, double totalMb) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
        return ((m.ullTotalPhys - m.ullAvailPhys) / 1_048_576.0, m.ullTotalPhys / 1_048_576.0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
