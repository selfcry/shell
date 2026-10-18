namespace ClubShell.Core;

public sealed record CleanResult(int Files, int Dirs, int Errors, long Bytes);

/// <summary>
/// Очистка профиля гостя после сеанса («как в Langame»: всё скачанное и сохранённое исчезает).
/// Запускается службой при загрузке Windows, пока гость ещё не вошёл и файлы не заняты.
/// Защита от ошибок настройки: чистится только папка C:\Users\&lt;GuestUser&gt;, внутри неё только перечисленные
/// подпапки, символические ссылки и junction не открываются (удаляется сама ссылка, а не то, на что она указывает).
/// </summary>
public static class ProfileCleaner
{
    // Имена учётных записей, которые нельзя чистить ни при каких настройках.
    private static readonly string[] Forbidden = { "administrator", "администратор", "default", "public", "all users", "defaultuser0", "system" };

    public static readonly string[] DefaultDirs =
    {
        "Downloads", "Documents", "Desktop", "Pictures", "Videos", "Music", "Recent",
        @"AppData\Local\Temp",
        @"AppData\Local\Microsoft\Windows\INetCache",
        @"AppData\Local\Google\Chrome\User Data",
        @"AppData\Local\Microsoft\Edge\User Data",
        @"AppData\Roaming\Mozilla\Firefox",
        @"AppData\Roaming\discord",
        @"AppData\Roaming\Telegram Desktop",
        @"AppData\Local\Yandex\YandexBrowser\User Data",
    };

    /// <summary>Возвращает null, если чистить нельзя (с причиной в <paramref name="refusal"/>).</summary>
    public static CleanResult? Clean(string usersRoot, string guestUser, IEnumerable<string> relDirs, out string? refusal)
    {
        refusal = Validate(usersRoot, guestUser, out var profile);
        if (refusal is not null) return null;

        int files = 0, dirs = 0, errors = 0;
        long bytes = 0;
        foreach (var rel in relDirs)
        {
            var target = Path.GetFullPath(Path.Combine(profile, rel.Replace('\\', Path.DirectorySeparatorChar)));
            // подпапка обязана лежать внутри профиля гостя и не быть самим профилем
            if (!IsInside(profile, target) || !Directory.Exists(target)) continue;
            ClearContents(target, ref files, ref dirs, ref errors, ref bytes);
        }
        return new CleanResult(files, dirs, errors, bytes);
    }

    private static string? Validate(string usersRoot, string guestUser, out string profile)
    {
        profile = "";
        if (string.IsNullOrWhiteSpace(guestUser)) return "не задан GuestUser";
        if (guestUser.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0 || guestUser.Contains("..")) return "недопустимое имя пользователя";
        if (Forbidden.Contains(guestUser.Trim().ToLowerInvariant())) return $"учётную запись «{guestUser}» чистить запрещено";
        if (guestUser.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase) && !OperatingSystem.IsWindows()) return "это текущий пользователь";

        var root = Path.GetFullPath(usersRoot);
        profile = Path.GetFullPath(Path.Combine(root, guestUser.Trim()));
        if (!IsInside(root, profile)) return "профиль вне папки пользователей";
        if (!Directory.Exists(profile)) return "профиль гостя не найден";
        return null;
    }

    private static bool IsInside(string parent, string child)
    {
        var p = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(p, StringComparison.OrdinalIgnoreCase) && child.Length > p.Length;
    }

    private static void ClearContents(string dir, ref int files, ref int dirs, ref int errors, ref long bytes)
    {
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            try
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    entry.Delete(); // ссылка: удаляем только её саму
                    files++;
                    continue;
                }
                if (entry is DirectoryInfo d)
                {
                    ClearContents(d.FullName, ref files, ref dirs, ref errors, ref bytes);
                    d.Attributes = FileAttributes.Normal;
                    d.Delete();
                    dirs++;
                }
                else if (entry is FileInfo f)
                {
                    bytes += f.Length;
                    f.Attributes = FileAttributes.Normal; // снимаем «только чтение»
                    f.Delete();
                    files++;
                }
            }
            catch (Exception) { errors++; }
        }
    }
}
