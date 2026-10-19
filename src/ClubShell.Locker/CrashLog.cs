namespace ClubShell.Locker;

/// <summary>Журнал ошибок оболочки: %LocalAppData%\ClubShell\locker.log (последние ~1 МБ).</summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClubShell");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "locker.log");
            lock (Gate)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception) { /* журнал не должен ронять оболочку */ }
    }
}
