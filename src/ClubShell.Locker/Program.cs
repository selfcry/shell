namespace ClubShell.Locker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Одно окно блокировки на сессию Windows.
        using var mutex = new Mutex(true, @"Local\ClubShell.Locker", out var isFirst);
        if (!isFirst) return;

        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemTaskbar.Restore(); // вернуть панель Windows при любом выходе
        ApplicationConfiguration.Initialize();
        Application.Run(new LockerForm());
    }
}
