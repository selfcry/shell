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

    private readonly TimeZoneInfo _tz;

    public MaintenanceWorker(ClubService club, Db db, TimeZoneInfo tz, IConfiguration cfg, ILogger<MaintenanceWorker> log)
    {
        (_club, _db, _tz, _log) = (club, db, tz, log);
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
                    _club.PruneLoginState();
                    _club.ExpireBookings(); // брони, на которые не пришли
                    var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz); // 04:00 по времени клуба, а не сервера
                    if (local.Hour == 4 && DateOnly.FromDateTime(local.DateTime) != _lastBackup) Backup(local);
                }
                catch (Exception ex) { _log.LogError(ex, "Maintenance tick failed"); }
            }
        }
        catch (OperationCanceledException) { }
        finally { try { _club.FlushPresence(); } catch { /* при остановке */ } }
    }

    private void Backup(DateTimeOffset local)
    {
        Directory.CreateDirectory(_backupDir);
        var file = Path.Combine(_backupDir, $"club-{local:yyyyMMdd}.db");
        if (File.Exists(file)) File.Delete(file);
        using var c = _db.Open();
        Db.Exec(c, "VACUUM INTO $f", ("$f", file));
        // проверяем, что копия читается: битая копия хуже, чем никакой
        using (var check = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"))
        {
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            var res = cmd.ExecuteScalar() as string;
            if (res != "ok") { _log.LogError("Резервная копия {File} повреждена: {Res}", file, res); return; }
        }
        foreach (var old in new DirectoryInfo(_backupDir).GetFiles("club-*.db").OrderByDescending(f => f.Name).Skip(14)) old.Delete();
        _lastBackup = DateOnly.FromDateTime(local.DateTime);
        _log.LogWarning("Резервная копия базы готова: {File}", file); // Warning: иначе при уровне журнала Warning успех не виден
    }
}
