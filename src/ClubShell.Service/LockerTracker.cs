using Microsoft.Win32.SafeHandles;

namespace ClubShell.Service;

/// <summary>
/// Окно блокировки, которое запустил страж: его PID и handle процесса (а не просто «процесс с таким именем»).
/// ControlPipeServer принимает статус и запросы только от этого процесса, SessionEnforcer считает от момента его запуска.
/// </summary>
public sealed class LockerTracker
{
    private readonly object _gate = new();
    private SafeProcessHandle? _handle;

    public int Pid { get; private set; }
    public uint SessionId { get; private set; }
    public DateTime StartedUtc { get; private set; } = DateTime.MinValue;

    public void Set(SafeProcessHandle handle, int pid, uint session)
    {
        lock (_gate)
        {
            _handle?.Dispose();
            (_handle, Pid, SessionId, StartedUtc) = (handle, pid, session, DateTime.UtcNow);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _handle?.Dispose();
            (_handle, Pid) = (null, 0);
        }
    }

    /// <summary>Отслеживаемый процесс жив и работает в этой сессии.</summary>
    public bool IsAlive(uint session)
    {
        lock (_gate)
        {
            return _handle is { IsInvalid: false, IsClosed: false } h && SessionId == session && SessionLauncher.IsProcessAlive(h);
        }
    }

    public bool IsTracked(int pid) => pid != 0 && pid == Pid;
}
