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

        // Одно окно блокировки на сессию Windows.
        using var mutex = new Mutex(true, @"Local\ClubShell.Locker", out var isFirst);
        if (!isFirst) return;

        SystemTaskbar.RestoreLeftover(); // если прошлый запуск закрыли силой, возвращаем панель Windows
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemTaskbar.Restore(); // вернуть панель Windows при любом выходе
        ApplicationConfiguration.Initialize();
        Application.Run(new LockerForm());
    }
}
