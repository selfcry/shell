namespace ClubShell.Service;

/// <summary>Общее состояние, которое пишут HeartbeatWorker и ControlPipeServer.</summary>
public sealed class LinkState
{
    private long _lastOkTicks = DateTime.UtcNow.Ticks;
    private volatile string? _user;
    private volatile bool _sessionActive;

    /// <summary>Связь считается потерянной, когда пульс не проходит дольше OfflineGraceSec.</summary>
    public volatile bool Offline;
    public string? Reason { get; set; }

    public DateTime LastOkUtc
    {
        get => new(Interlocked.Read(ref _lastOkTicks), DateTimeKind.Utc);
        set => Interlocked.Exchange(ref _lastOkTicks, value.Ticks);
    }

    public string? User => _user;
    public bool SessionActive => _sessionActive;

    public void SetStatus(string? user, bool sessionActive)
    {
        _user = user;
        _sessionActive = sessionActive;
    }
}
