namespace ClubShell.Service;

/// <summary>Общее состояние, которое пишут HeartbeatWorker, ControlPipeServer и ServerLinkWorker, а читает SessionEnforcer.</summary>
public sealed class LinkState
{
    private long _lastOkTicks = DateTime.UtcNow.Ticks;
    private long _lockerStatusTicks;
    private volatile string? _user;
    private volatile int _serverSession = -1; // -1 неизвестно, 0 нет сеанса, 1 есть
    private volatile bool _lockerLocked;
    private volatile bool _lockerSession;

    /// <summary>Связь считается потерянной, когда пульс не проходит дольше OfflineGraceSec.</summary>
    public volatile bool Offline;
    public string? Reason { get; set; }

    public DateTime LastOkUtc
    {
        get => new(Interlocked.Read(ref _lastOkTicks), DateTimeKind.Utc);
        set => Interlocked.Exchange(ref _lastOkTicks, value.Ticks);
    }

    /// <summary>Гость, вошедший на этом ПК через службу (по ответу сервера, а не со слов окна). Уходит в пульс.</summary>
    public string? User => _user;
    public void SetUser(string? user) => _user = user;

    /// <summary>По данным сервера на этом ПК идёт оплаченный сеанс. null — сервер ещё не ответил или не сообщает.</summary>
    public bool? ServerSessionActive => _serverSession switch { 1 => true, 0 => false, _ => null };
    public void SetServerSession(bool? active) => _serverSession = active switch { true => 1, false => 0, null => -1 };

    /// <summary>Последний статус от окна блокировки, запущенного стражем (от других клиентов канала не принимается).</summary>
    public DateTime LockerStatusUtc => new(Interlocked.Read(ref _lockerStatusTicks), DateTimeKind.Utc);
    public bool LockerLocked => _lockerLocked;
    public bool LockerSessionActive => _lockerSession;

    public void SetLockerStatus(bool locked, bool sessionActive)
    {
        _lockerLocked = locked;
        _lockerSession = sessionActive;
        Interlocked.Exchange(ref _lockerStatusTicks, DateTime.UtcNow.Ticks);
    }
}
