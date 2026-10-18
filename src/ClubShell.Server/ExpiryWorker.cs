namespace ClubShell.Server;

/// <summary>Раз в 2 секунды закрывает сессии, у которых вышло время, и командой по WebSocket просит ПК завершить сеанс.</summary>
public sealed class ExpiryWorker : BackgroundService
{
    private readonly ClubService _club;
    private readonly PcHub _hub;
    private readonly ILogger<ExpiryWorker> _log;

    public ExpiryWorker(ClubService club, PcHub hub, ILogger<ExpiryWorker> log) => (_club, _hub, _log) = (club, hub, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                foreach (var s in _club.Expired())
                {
                    if (_club.EndSession(s.PcId, "Время вышло") is null) continue;
                    _log.LogInformation("Сеанс {Id} ({Guest} на {Pc}) завершён: время вышло", s.Id, s.Guest, s.Pc);
                    await _hub.SendAsync(s.PcId, new { type = "command", name = "endSession", text = "Время вышло" });
                }
            }
            catch (Exception ex) { _log.LogError(ex, "Expiry tick failed"); }
        }
    }
}
