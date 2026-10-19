using System.Diagnostics;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Страж: раз в 2 секунды проверяет, что окно блокировки, запущенное им самим, живо в активной сессии Windows,
/// и запускает его заново, если оно закрыто или убито. Следит по handle своего процесса, а не по имени:
/// процесс гостя, названный ClubShell.Locker.exe, окном блокировки не считается и закрывается.
/// Пока есть файл maintenance (после выхода администратора по PIN) — не вмешивается.
/// </summary>
public sealed class GuardianWorker : BackgroundService
{
    private const string LockerName = "ClubShell.Locker";
    private readonly ILogger<GuardianWorker> _log;
    private readonly LockerTracker _tracker;
    private readonly string _lockerPath = Path.Combine(AppContext.BaseDirectory, "locker", "ClubShell.Locker.exe");

    public GuardianWorker(LockerTracker tracker, ILogger<GuardianWorker> log) => (_tracker, _log) = (tracker, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _log.LogInformation("Guardian started, locker: {Path}", _lockerPath);
        // Режим обслуживания живёт до перезагрузки: после неё ПК снова под защитой, даже если админ забыл выйти.
        try { File.Delete(ShellConfig.MaintenanceFlag); } catch (Exception) { /* нет файла */ }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var firstTick = true;

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
                if (SessionLauncher.ActiveSession() is not { } session || !SessionLauncher.HasUser(session)) continue; // никто не вошёл — запускать некуда

                if (_tracker.IsAlive(session)) { KillImpostors(session); continue; }
                _tracker.Clear();

                // Служба перезапустилась, а окно осталось: берём под надзор, если это наш файл.
                if (firstTick && TryAdopt(session)) { firstTick = false; continue; }
                firstTick = false;

                KillImpostors(session); // иначе чужой процесс держит мьютекс окна, и наше сразу закроется
                _log.LogWarning("Locker is not running in the active session, starting");
                if (SessionLauncher.StartInActiveSession(_lockerPath, out var handle, out var pid, out var started, out var error))
                    _tracker.Set(handle!, pid, started);
                else
                    _log.LogWarning("Start failed: {Error}", error);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Guardian tick failed");
            }
        }
    }

    private bool TryAdopt(uint session)
    {
        foreach (var p in Process.GetProcessesByName(LockerName))
        {
            using (p)
            {
                if ((uint)p.SessionId != session) continue;
                var h = SessionLauncher.Open(p.Id);
                if (h is null) continue;
                if (SessionLauncher.SamePath(SessionLauncher.ImagePath(h), _lockerPath))
                {
                    _tracker.Set(h, p.Id, session);
                    _log.LogInformation("Adopted running locker pid {Pid}", p.Id);
                    return true;
                }
                h.Dispose();
            }
        }
        return false;
    }

    /// <summary>Всё, что называется как окно блокировки, но не запущено стражем, — подделка или лишняя копия.</summary>
    private void KillImpostors(uint session)
    {
        foreach (var p in Process.GetProcessesByName(LockerName))
        {
            using (p)
            {
                if ((uint)p.SessionId != session || _tracker.IsTracked(p.Id)) continue;
                try
                {
                    p.Kill(entireProcessTree: true);
                    _log.LogWarning("Killed foreign {Name} pid {Pid}", LockerName, p.Id);
                }
                catch (Exception ex) { _log.LogWarning("Cannot kill foreign locker pid {Pid}: {Message}", p.Id, ex.Message); }
            }
        }
    }
}
