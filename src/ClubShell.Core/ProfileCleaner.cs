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
        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup", // автозапуск, подложенный прошлым гостем
        @"AppData\Roaming\Microsoft\Credentials",                            // сохранённые пароли Windows
        @"AppData\Local\Microsoft\Credentials",
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
            // Ни сама папка, ни её родители до профиля не должны быть ссылкой: гость мог заменить Downloads на junction
            // к C:\ProgramData\ClubShell, и служба (SYSTEM) стёрла бы чужое. Папку-ссылку удаляем как ссылку.
            if (ReparseOnPath(profile, target, out var link))
            {
                if (link == target) try { Directory.Delete(target); files++; } catch (Exception) { errors++; }
                else errors++;
                continue;
            }
            ClearContents(target, ref files, ref dirs, ref errors, ref bytes);
        }
        return new CleanResult(files, dirs, errors, bytes);
    }

    /// <summary>Эталонный реестр гостя (снимок, сделанный harden.ps1 -SaveGolden): лежит в папке службы, гостю недоступен.</summary>
    public static string GoldenHive => Path.Combine(ShellConfig.Dir, "golden", "NTUSER.DAT");

    /// <summary>
    /// «Неизменяемый профиль»: вернуть реестр гостя (HKCU) к эталону. Всё, что прошлый гость записал в HKCU —
    /// автозапуск, своя оболочка (Winlogon\Shell), переменные окружения, пути папок — пропадает.
    /// Вызывать до входа гостя (файл реестра в это время не занят). Журналы транзакций удаляются, иначе Windows «доиграет» их.
    /// </summary>
    public static string? RestoreGoldenHive(string usersRoot, string guestUser)
    {
        var refusal = Validate(usersRoot, guestUser, out var profile);
        if (refusal is not null) return refusal;
        if (!File.Exists(GoldenHive)) return "нет эталона (harden.ps1 -SaveGolden)";
        var hive = Path.Combine(profile, "NTUSER.DAT");
        if (File.Exists(hive) && new FileInfo(hive).Attributes.HasFlag(FileAttributes.ReparsePoint)) return "NTUSER.DAT — ссылка";
        try
        {
            using (File.Open(hive, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { } // занят — гость уже вошёл
        }
        catch (IOException) { return "реестр гостя занят: гость вошёл, восстановление после перезагрузки"; }
        foreach (var f in Directory.EnumerateFiles(profile, "ntuser.dat*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (name.Equals("NTUSER.DAT", StringComparison.OrdinalIgnoreCase)) continue;
            try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch (Exception) { /* занятые журналы оставляем */ }
        }
        File.SetAttributes(hive, FileAttributes.Normal);
        File.Copy(GoldenHive, hive, overwrite: true);
        File.SetAttributes(hive, FileAttributes.Hidden | FileAttributes.System);
        return null;
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
        if (new DirectoryInfo(profile).Attributes.HasFlag(FileAttributes.ReparsePoint)) return "профиль гостя — ссылка, чистить нельзя";
        return null;
    }

    /// <summary>Есть ли ссылка (junction/symlink) среди папок от профиля (включительно) до target. link — первая найденная.</summary>
    private static bool ReparseOnPath(string profile, string target, out string? link)
    {
        link = null;
        var cur = target;
        while (true)
        {
            try
            {
                if (new DirectoryInfo(cur).Attributes.HasFlag(FileAttributes.ReparsePoint)) link = cur; // запоминаем ближайшую к профилю
            }
            catch (Exception) { link = cur; return true; } // не смогли проверить — не трогаем
            if (string.Equals(cur.TrimEnd(Path.DirectorySeparatorChar), profile.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            var parent = Path.GetDirectoryName(cur);
            if (parent is null || parent.Length < profile.Length) { link = cur; return true; }
            cur = parent;
        }
        return link is not null;
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
