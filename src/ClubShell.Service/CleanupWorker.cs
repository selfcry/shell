using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Один раз при запуске службы (то есть при загрузке Windows, до входа гостя) удаляет файлы гостя
/// от прошлого сеанса. Регистрируется раньше стража, чтобы окно блокировки не стартовало поверх чистки.
/// </summary>
public sealed class CleanupWorker : IHostedService
{
    private readonly ShellConfig _cfg;
    private readonly ILogger<CleanupWorker> _log;

    public CleanupWorker(ShellConfig cfg, ILogger<CleanupWorker> log) => (_cfg, _log) = (cfg, log);

    public Task StartAsync(CancellationToken ct)
    {
        if (!_cfg.WipeOnBoot) { _log.LogInformation("Очистка профиля гостя выключена"); return Task.CompletedTask; }
        try
        {
            var usersRoot = Path.Combine(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\", "Users");
            var dirs = _cfg.WipeDirs.Count > 0 ? _cfg.WipeDirs : ProfileCleaner.DefaultDirs.ToList();
            // сначала реестр (неизменяемый профиль), потом файлы
            if (_cfg.RestoreGoldenHive)
            {
                var hiveRefusal = ProfileCleaner.RestoreGoldenHive(usersRoot, _cfg.GuestUser);
                if (hiveRefusal is null) _log.LogInformation("Реестр гостя возвращён к эталону");
                else _log.LogWarning("Реестр гостя не восстановлен: {Reason}", hiveRefusal);
            }
            var result = ProfileCleaner.Clean(usersRoot, _cfg.GuestUser, dirs, out var refusal);
            if (result is null) _log.LogWarning("Очистка профиля гостя не выполнена: {Reason}", refusal);
            else _log.LogInformation("Профиль гостя очищен: файлов {Files}, папок {Dirs}, {Mb} МБ, ошибок {Errors}",
                result.Files, result.Dirs, result.Bytes / 1_048_576, result.Errors);
        }
        catch (Exception ex) { _log.LogError(ex, "Очистка профиля гостя упала"); }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
