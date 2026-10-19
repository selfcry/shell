using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Страж: раз в 2 секунды проверяет, что окно блокировки запущено в активной сессии Windows,
/// и запускает его заново, если оно закрыто или убито. Пока есть файл maintenance (после выхода
/// администратора по PIN) — не вмешивается.
/// </summary>
public sealed class GuardianWorker : BackgroundService
{
    private readonly ILogger<GuardianWorker> _log;
    private readonly string _lockerPath = Path.Combine(AppContext.BaseDirectory, "locker", "ClubShell.Locker.exe");

    public GuardianWorker(ILogger<GuardianWorker> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("Guardian started, locker: {Path}", _lockerPath);
        // Режим обслуживания живёт до перезагрузки: после неё ПК снова под защитой, даже если админ забыл выйти.
        try { File.Delete(ShellConfig.MaintenanceFlag); } catch (Exception) { /* нет файла */ }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                if (File.Exists(ShellConfig.MaintenanceFlag))
                {
                    // и не дольше 30 минут
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(ShellConfig.MaintenanceFlag) < TimeSpan.FromMinutes(30)) continue;
                    File.Delete(ShellConfig.MaintenanceFlag);
                    _log.LogWarning("Maintenance mode expired");
                }
                if (SessionLauncher.IsRunningInActiveSession("ClubShell.Locker")) continue;

                _log.LogWarning("Locker is not running in the active session, starting");
                if (!SessionLauncher.StartInActiveSession(_lockerPath, out var error))
                    _log.LogWarning("Start failed: {Error}", error);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Guardian tick failed");
            }
        }
    }
}
