namespace ClubShell.Core;

/// <summary>Состояние сессии гостя: отсчёт времени и баланс. От интерфейса не зависит.</summary>
public sealed class SessionManager : IDisposable
{
    private readonly IClubApi _api;
    private readonly System.Threading.Timer _timer;
    private int _remainingSec;

    /// <summary>Имя ПК для сервера; окно блокировки уточняет его, когда служба пришлёт hello.</summary>
    public string PcId { get; set; }

    public string? User { get; private set; }
    public int Balance { get; private set; }
    public bool Active => _remainingSec > 0 && User is not null;

    public event Action<int, int>? Tick;   // осталось секунд, баланс
    public event Action<string>? Ended;    // причина завершения

    public SessionManager(IClubApi api, string pcId)
    {
        _api = api;
        PcId = pcId;
        _timer = new System.Threading.Timer(_ => OnSecond(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Гость вышел, не начав сеанс.</summary>
    public void Logout()
    {
        if (!Active) { User = null; Balance = 0; }
    }

    public void SetUser(string user, int balance) { User = user; Balance = balance; }

    /// <summary>Баланс изменился на сервере (пополнение или списание администратором).</summary>
    public void SetBalance(int balance) => Balance = balance;

    public void Begin(int minutes, int balance)
    {
        Balance = balance;
        _remainingSec = minutes * 60;
        _timer.Change(1000, 1000);
    }

    public async Task StopAsync(string reason)
    {
        var user = User;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _remainingSec = 0;
        User = null;
        if (user is null) return;
        // Сначала блокировка, потом отчёт серверу: пока запрос идёт (до 6 с при плохой связи), гость не должен оставаться в сеансе.
        Ended?.Invoke(reason);
        StopReport = ReportStopAsync(user);
        await StopReport;
    }

    /// <summary>Отчёт серверу о последнем завершении: перезагрузку в конце сеанса ждут, пока он не уйдёт (но недолго).</summary>
    public Task StopReport { get; private set; } = Task.CompletedTask;

    private async Task ReportStopAsync(string user)
    {
        try { await _api.StopSessionAsync(PcId, user); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ClubShell] stop report failed: {ex.Message}"); }
    }

    /// <summary>Сервер уточнил остаток времени (например, админ добавил минуты).</summary>
    public void SyncRemaining(int seconds)
    {
        if (Active && seconds > 0) Interlocked.Exchange(ref _remainingSec, seconds);
    }

    private void OnSecond()
    {
        if (--_remainingSec <= 0) { _ = StopAsync("Время вышло"); return; }
        Tick?.Invoke(_remainingSec, Balance);
    }

    public void Dispose() => _timer.Dispose();
}
