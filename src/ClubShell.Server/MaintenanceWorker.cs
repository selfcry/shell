namespace ClubShell.Server;

/// <summary>
/// Фоновое обслуживание базы: каждые 30 с записывает накопленные пульсы ПК одной транзакцией,
/// каждую ночь делает резервную копию (VACUUM INTO) и хранит последние 7.
/// </summary>
public sealed class MaintenanceWorker : BackgroundService
{
    private readonly ClubService _club;
    private readonly Db _db;
    private readonly string _backupDir;
    private readonly ILogger<MaintenanceWorker> _log;
    private DateOnly _lastBackup;

    public MaintenanceWorker(ClubService club, Db db, IConfiguration cfg, ILogger<MaintenanceWorker> log)
    {
        (_club, _db, _log) = (club, db, log);
        _backupDir = Path.Combine(cfg["Club:DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "data"), "backups");
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    _club.FlushPresence();
                    if (DateTime.Now.Hour == 4 && DateOnly.FromDateTime(DateTime.Now) != _lastBackup) Backup();
                }
                catch (Exception ex) { _log.LogError(ex, "Maintenance tick failed"); }
            }
        }
        catch (OperationCanceledException) { }
        finally { try { _club.FlushPresence(); } catch { /* при остановке */ } }
    }

    private void Backup()
    {
        Directory.CreateDirectory(_backupDir);
        var file = Path.Combine(_backupDir, $"club-{DateTime.Now:yyyyMMdd}.db");
        if (File.Exists(file)) File.Delete(file);
        using var c = _db.Open();
        Db.Exec(c, "VACUUM INTO $f", ("$f", file));
        foreach (var old in new DirectoryInfo(_backupDir).GetFiles("club-*.db").OrderByDescending(f => f.Name).Skip(7)) old.Delete();
        _lastBackup = DateOnly.FromDateTime(DateTime.Now);
        _log.LogInformation("Резервная копия базы: {File}", file);
    }
}
