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

        // Одно окно блокировки на сессию Windows.
        using var mutex = new Mutex(true, @"Local\ClubShell.Locker", out var isFirst);
        if (!isFirst) return;

        SystemTaskbar.RestoreLeftover(); // если прошлый запуск закрыли силой, возвращаем панель Windows
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemTaskbar.Restore(); // вернуть панель Windows при любом выходе
        ApplicationConfiguration.Initialize();
        Application.Run(new LockerForm());
    }
}
