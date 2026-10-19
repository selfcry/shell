using System.Diagnostics;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Контроль сеанса на стороне службы. Раньше конец сеанса держался только на окне блокировки, а оно работает под гостем:
/// его можно приостановить, подменить или «заморозить» отладчиком — и играть дальше бесплатно.
/// Теперь служба сама сверяет: если по данным сервера на ПК нет оплаченного сеанса (или связь с сервером потеряна),
/// а окно, запущенное стражем, EnforceLockSec секунд не подтверждает по каналу «заблокировано» — служба завершает
/// сеанс Windows гостя (WTSLogoffSession, закрываются все его процессы) и перезагружает ПК (при загрузке профиль чистится).
/// </summary>
public sealed class SessionEnforcer : BackgroundService
{
    /// <summary>Статус окно шлёт раз в 2 с; старше — окно молчит.</summary>
    private static readonly TimeSpan StatusFresh = TimeSpan.FromSeconds(6);
    /// <summary>После входа в Windows окну нужно время запуститься (WebView2, первое подключение к каналу).</summary>
    private static readonly TimeSpan LogonGrace = TimeSpan.FromSeconds(60);
    /// <summary>После принудительного завершения ждём перезагрузку, а не повторяем.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private readonly ShellConfig _config;
    private readonly LinkState _link;
    private readonly LockerTracker _tracker;
    private readonly ILogger<SessionEnforcer> _log;

    private uint? _session;
    private DateTime _sessionSinceUtc;
    private DateTime? _violationSinceUtc;
    private DateTime _cooldownUntilUtc = DateTime.MinValue;

    public SessionEnforcer(ShellConfig config, LinkState link, LockerTracker tracker, ILogger<SessionEnforcer> log)
        => (_config, _link, _tracker, _log) = (config, link, tracker, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (_config.EnforceLockSec <= 0) { _log.LogWarning("Session enforcement is off (EnforceLockSec = 0)"); return; }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try { Tick(); }
            catch (Exception ex) { _log.LogError(ex, "Session enforcement tick failed"); }
        }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        if (now < _cooldownUntilUtc) return;

        // Обслуживание по PIN администратора: окна нет намеренно.
        if (File.Exists(ShellConfig.MaintenanceFlag)) { Reset(); return; }

        if (SessionLauncher.ActiveSession() is not { } session || !SessionLauncher.HasUser(session)) { _session = null; Reset(); return; }
        if (_session != session) { _session = session; _sessionSinceUtc = now; Reset(); }

        // Должен ли ПК сейчас быть заблокирован? Сеанс идёт или сервер ещё не ответил — окно не трогаем.
        var shouldLock = _link.Offline || _link.ServerSessionActive == false;
        if (!shouldLock) { Reset(); return; }

        var confirmed = _link.LockerLocked && now - _link.LockerStatusUtc < StatusFresh && _tracker.IsAlive(session);
        if (confirmed) { Reset(); return; }
        if (now - _sessionSinceUtc < LogonGrace) return;

        if (_violationSinceUtc is null)
        {
            _violationSinceUtc = now;
            _log.LogWarning("No paid session ({Why}) but the locker does not confirm the locked state; enforcing in {Sec}s",
                _link.Offline ? "server offline" : "server", _config.EnforceLockSec);
            return;
        }
        if (now - _violationSinceUtc < TimeSpan.FromSeconds(_config.EnforceLockSec)) return;

        Enforce(session);
        _cooldownUntilUtc = now + Cooldown;
        Reset();
    }

    private void Reset() => _violationSinceUtc = null;

    private void Enforce(uint session)
    {
        _log.LogCritical("Locker did not confirm the locked state for {Sec}s without a paid session: logging off session {Session} and rebooting",
            _config.EnforceLockSec, session);
        if (!SessionLauncher.Logoff(session, out var error)) _log.LogError("Logoff failed: {Error}", error);
        try { Process.Start(new ProcessStartInfo("shutdown", "/r /t 5 /f") { CreateNoWindow = true, UseShellExecute = false }); }
        catch (Exception ex) { _log.LogError(ex, "Reboot failed"); }
    }
}
