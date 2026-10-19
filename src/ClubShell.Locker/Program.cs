using ClubShell.Core;

namespace ClubShell.Locker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Аварийная команда: вернуть панель Windows после принудительного закрытия оболочки.
        if (args.Contains("--restore-taskbar", StringComparer.OrdinalIgnoreCase))
        {
            SystemTaskbar.ForceRestore();
            return;
        }

        // Переменные WebView2 из окружения гостя (свой «браузер», --remote-debugging-port) не должны влиять на оболочку.
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(n => n.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase)).ToList())
            Environment.SetEnvironmentVariable(name, null);

        // Необработанная ошибка не должна показывать гостю окно «Продолжить / Выход»: пишем в журнал и продолжаем.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);

        // Одно окно блокировки. На боевом ПК мьютекс в Global\ создаёт служба (SYSTEM) с ограниченными правами:
        // гость не может ни создать это имя заранее, ни запретить к нему доступ (обычный Local\ он мог занять своим процессом).
        using var mutex = AcquireSingleInstance();
        if (mutex is null) return;

        SystemTaskbar.RestoreLeftover(); // если прошлый запуск закрыли силой, возвращаем панель Windows
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemTaskbar.Restore(); // вернуть панель Windows при любом выходе
        ApplicationConfiguration.Initialize();
        Application.Run(new LockerForm());
    }

    private static Mutex? AcquireSingleInstance()
    {
        Mutex? mutex;
        try { if (!Mutex.TryOpenExisting(PipeNames.LockerMutex, out mutex)) mutex = null; }
        catch (UnauthorizedAccessException) { mutex = null; }
        if (mutex is null)
        {
            // Службы нет (тестовый запуск) или она ещё не стартовала — обычный мьютекс сессии.
            mutex = new Mutex(false, @"Local\ClubShell.Locker");
        }
        try
        {
            // Прошлое окно могло только что завершаться (перезапуск стражем) — ждём его немного.
            if (mutex.WaitOne(TimeSpan.FromSeconds(5))) return mutex;
        }
        catch (AbandonedMutexException) { return mutex; } // прошлое окно убито: мьютекс теперь наш
        mutex.Dispose();
        return null;
    }
}
