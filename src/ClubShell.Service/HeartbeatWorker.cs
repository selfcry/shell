using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Раз в 5 секунд отправляет серверу пульс этого ПК. Если сервер не отвечает дольше OfflineGraceSec,
/// выставляет LinkState.Offline — ControlPipeServer передаст это окну блокировки, и оно закроет сессию.
/// Ответ сервера (идёт ли на ПК оплаченный сеанс) запоминает для SessionEnforcer.
/// </summary>
public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IClubApi _api;
    private readonly ShellConfig _config;
    private readonly LinkState _link;
    private readonly ILogger<HeartbeatWorker> _log;

    public HeartbeatWorker(IClubApi api, ShellConfig config, LinkState link, ILogger<HeartbeatWorker> log)
        => (_api, _config, _link, _log) = (api, config, link, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
                cts.CancelAfter(TimeSpan.FromSeconds(4)); // зависший запрос не должен останавливать проверку
                var info = await _api.HeartbeatAsync(_config.PcName, _link.User).WaitAsync(cts.Token);
                if (info is not null)
                {
                    if (_link.ServerSessionActive == true && !info.SessionActive) _log.LogInformation("Server reports no active session on this PC");
                    _link.SetServerSession(info.SessionActive);
                }

                _link.LastOkUtc = DateTime.UtcNow;
                if (_link.Offline) _log.LogInformation("Server link restored");
                _link.Offline = false;
                _link.Reason = null;
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                _log.LogWarning("Heartbeat failed: {Message}", ex.Message);
            }

            var silent = DateTime.UtcNow - _link.LastOkUtc;
            if (!_link.Offline && silent > TimeSpan.FromSeconds(_config.OfflineGraceSec))
            {
                _link.Reason = "Нет связи с сервером";
                _link.Offline = true;
                _log.LogError("No heartbeat for {Seconds:F0}s, locking this PC", silent.TotalSeconds);
            }
        }
        while (await WaitNext(timer, stop));
    }

    private static async Task<bool> WaitNext(PeriodicTimer timer, CancellationToken stop)
    {
        try { return await timer.WaitForNextTickAsync(stop); }
        catch (OperationCanceledException) { return false; }
    }
}
