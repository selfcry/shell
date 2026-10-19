using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace ClubShell.Core;

/// <summary>
/// Ищет установленную программу по имени exe: реестр (список установленных программ, App Paths),
/// затем типичные папки на всех дисках и неглубокий обход корней дисков. Результат кэшируется.
/// В каталоге записывается как "find:RentalGames.exe".
/// </summary>
public static class ExeFinder
{
    public const string Prefix = "find:";
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Find(string exeName)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(exeName, out var cached) && (cached is null || File.Exists(cached))) return cached; // найденное и «не найдено» кэшируются до Reset()
            var found = FromRegistry(exeName) ?? FromFolders(exeName);
            // Найденное принимаем, только если гость не может подменить файл (иначе он подложит свой exe следующему гостю).
            if (found is not null && !IsProtected(found)) found = null;
            Cache[exeName] = found;
            return found;
        }
    }

    /// <summary>Сбросить кэш (например, после установки программы).</summary>
    public static void Reset() { lock (Cache) Cache.Clear(); }

    private static string? FromRegistry(string exeName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var stem = Path.GetFileNameWithoutExtension(exeName);
        try
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine }) // HKCU гость пишет сам — ему не верим
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);

                using (var apps = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}"))
                    if (apps?.GetValue(null) is string p && File.Exists(p.Trim('"'))) return p.Trim('"');

                using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var k = uninstall.OpenSubKey(name);
                    var display = k?.GetValue("DisplayName") as string ?? "";
                    var location = (k?.GetValue("InstallLocation") as string)?.Trim('"');
                    var icon = (k?.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"');
                    if (!display.Contains(stem, StringComparison.OrdinalIgnoreCase) && !name.Contains(stem, StringComparison.OrdinalIgnoreCase)) continue;

                    if (!string.IsNullOrEmpty(location) && Directory.Exists(location) && Locate(location, exeName, 2) is { } a) return a;
                    if (!string.IsNullOrEmpty(icon) && Path.GetDirectoryName(icon) is { Length: > 0 } dir && Locate(dir, exeName, 1) is { } b) return b;
                }
            }
        }
        catch (Exception) { /* реестр недоступен — идём дальше по папкам */ }
        return null;
    }

    /// <summary>Ни файл, ни его папка не дают права записи обычным пользователям.</summary>
    public static bool IsProtected(string path)
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var dir = Path.GetDirectoryName(path);
            return !GuestCanWrite(new FileInfo(path).GetAccessControl()) && (dir is null || !GuestCanWrite(new DirectoryInfo(dir).GetAccessControl()));
        }
        catch (Exception) { return false; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool GuestCanWrite(FileSystemSecurity sec)
    {
        const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes | FileSystemRights.DeleteSubdirectoriesAndFiles;
        var guests = new[] { WellKnownSidType.BuiltinUsersSid, WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.WorldSid, WellKnownSidType.InteractiveSid }
            .Select(t => new SecurityIdentifier(t, null)).ToList();
        foreach (FileSystemAccessRule rule in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if (rule.IdentityReference is SecurityIdentifier sid && guests.Contains(sid) && (rule.FileSystemRights & WriteRights) != 0) return true;
        }
        return false;
    }

    private static string? FromFolders(string exeName)
    {
        var stem = Path.GetFileNameWithoutExtension(exeName);
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
        foreach (var root in drives)
        {
            foreach (var rel in new[] { stem, $@"Program Files\{stem}", $@"Program Files (x86)\{stem}", $@"ProgramData\{stem}", $@"Games\{stem}" })
            {
                var dir = Path.Combine(root, rel);
                if (Directory.Exists(dir) && Locate(dir, exeName, 2) is { } hit) return hit;
            }
        }
        // запасной вариант: на каждом диске смотрим папки первого уровня, в них — ещё один уровень
        foreach (var root in drives)
            if (Locate(root, exeName, 2, skipSystem: true) is { } hit) return hit;
        return null;
    }

    private static string? Locate(string dir, string exeName, int depth, bool skipSystem = false)
    {
        try
        {
            var direct = Path.Combine(dir, exeName);
            if (File.Exists(direct)) return direct;
            if (depth <= 0) return null;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (skipSystem && (name.StartsWith('$') || name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))) continue;
                if (Locate(sub, exeName, depth - 1, skipSystem) is { } hit) return hit;
            }
        }
        catch (Exception) { /* нет доступа к папке — пропускаем */ }
        return null;
    }
}
