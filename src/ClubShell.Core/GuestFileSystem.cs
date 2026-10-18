namespace ClubShell.Core;

public sealed record FsEntry(string Name, bool IsDir, long Size, long Modified);
public sealed record FsRoot(string Name, string Path);

public sealed class FsException : Exception
{
    public FsException(string message) : base(message) { }
}

/// <summary>
/// Файлы гостя для собственного проводника. Видно и можно менять только внутри разрешённых корней (папки профиля, внешние диски):
/// системные папки, чужие профили и AppData недоступны. Любой путь от страницы проверяется заново, символические ссылки и junction
/// не открываются (иначе ссылкой в Загрузках можно было бы выйти за пределы корня).
/// </summary>
public sealed class GuestFileSystem
{
    private static readonly char[] BadNameChars = Path.GetInvalidFileNameChars();
    private static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "LPT1", "LPT2", "LPT3" };
    private static readonly StringComparison PathCmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public IReadOnlyList<FsRoot> Roots { get; }

    public GuestFileSystem(IEnumerable<FsRoot> roots) =>
        Roots = roots.Select(r => new FsRoot(r.Name, Path.GetFullPath(r.Path).TrimEnd(Path.DirectorySeparatorChar))).Where(r => Directory.Exists(r.Path + Path.DirectorySeparatorChar)).ToList();

    private static bool Inside(string root, string full) =>
        full.Equals(root, PathCmp) || full.StartsWith(root + Path.DirectorySeparatorChar, PathCmp);

    /// <summary>Полный путь, если он внутри корня и по дороге нет ссылок. Иначе FsException.</summary>
    public string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) throw new FsException("Недопустимый путь");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Roots.FirstOrDefault(r => Inside(r.Path, full)) ?? throw new FsException("Эта папка недоступна");
        // проверяем каждый уровень от корня до цели: ссылка (junction/symlink) в середине пути — отказ
        var cur = root.Path;
        foreach (var part in full.Length > root.Path.Length ? full[(root.Path.Length + 1)..].Split(Path.DirectorySeparatorChar) : Array.Empty<string>())
        {
            cur = Path.Combine(cur, part);
            if (!File.Exists(cur) && !Directory.Exists(cur)) break;
            if (new FileInfo(cur).Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new FsException("Ссылки не поддерживаются");
        }
        return full;
    }

    private static string CheckName(string name)
    {
        name = (name ?? "").Trim().TrimEnd('.', ' ');
        if (name.Length is 0 or > 120 || name.IndexOfAny(BadNameChars) >= 0 || name is "." or ".." || Reserved.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            throw new FsException("Недопустимое имя");
        return name;
    }

    public List<FsEntry> List(string path)
    {
        var dir = Resolve(path);
        if (!Directory.Exists(dir)) throw new FsException("Папка не найдена");
        var result = new List<FsEntry>();
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            try
            {
                if (e.Attributes.HasFlag(FileAttributes.Hidden) || e.Attributes.HasFlag(FileAttributes.System) || e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (e.Name.StartsWith('.') && !OperatingSystem.IsWindows()) continue;
                var isDir = e is DirectoryInfo;
                result.Add(new FsEntry(e.Name, isDir, isDir ? 0 : ((FileInfo)e).Length, new DateTimeOffset(e.LastWriteTimeUtc).ToUnixTimeSeconds()));
            }
            catch (Exception) { /* файл исчез или недоступен */ }
        }
        return result.OrderByDescending(x => x.IsDir).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public string CreateFolder(string parent, string name)
    {
        var target = Resolve(Path.Combine(Resolve(parent), CheckName(name)));
        if (Directory.Exists(target) || File.Exists(target)) throw new FsException("Такое имя уже есть");
        Directory.CreateDirectory(target);
        return target;
    }

    public string Rename(string path, string newName)
    {
        var src = Resolve(path);
        if (Roots.Any(r => r.Path.Equals(src, PathCmp))) throw new FsException("Эту папку нельзя переименовать");
        var dst = Resolve(Path.Combine(Path.GetDirectoryName(src)!, CheckName(newName)));
        if (File.Exists(dst) || Directory.Exists(dst)) throw new FsException("Такое имя уже есть");
        if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst);
        return dst;
    }

    public void Delete(string path)
    {
        var p = Resolve(path);
        if (Roots.Any(r => r.Path.Equals(p, PathCmp))) throw new FsException("Эту папку нельзя удалить");
        if (Directory.Exists(p)) DeleteTree(p);
        else if (File.Exists(p)) { File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); }
        else throw new FsException("Не найдено");
    }

    private static void DeleteTree(string dir)
    {
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) { e.Delete(); continue; } // ссылку удаляем саму, в неё не заходим
            if (e is DirectoryInfo d) DeleteTree(d.FullName);
            else { e.Attributes = FileAttributes.Normal; e.Delete(); }
        }
        File.SetAttributes(dir, FileAttributes.Normal);
        Directory.Delete(dir);
    }

    private string FreeName(string dir, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = Path.Combine(dir, name);
        for (var i = 1; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, i == 1 ? $"{stem} (копия){ext}" : $"{stem} (копия {i}){ext}");
        return candidate;
    }

    /// <summary>Копирует файл или папку в другую папку. Занятое имя заменяется на «имя (копия)».</summary>
    public string Copy(string source, string destDir)
    {
        var src = Resolve(source);
        var dir = Resolve(destDir);
        if (!Directory.Exists(dir)) throw new FsException("Папка назначения не найдена");
        if (Directory.Exists(src) && Inside(src, dir)) throw new FsException("Нельзя скопировать папку внутрь самой себя");
        var dst = FreeName(dir, Path.GetFileName(src));
        if (Directory.Exists(src)) CopyTree(src, dst); else File.Copy(src, dst);
        return dst;
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var e in new DirectoryInfo(src).EnumerateFileSystemInfos())
        {
            if (e.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var target = Path.Combine(dst, e.Name);
            if (e is DirectoryInfo d) CopyTree(d.FullName, target); else ((FileInfo)e).CopyTo(target);
        }
    }

    public string Move(string source, string destDir)
    {
        var src = Resolve(source);
        if (Roots.Any(r => r.Path.Equals(src, PathCmp))) throw new FsException("Эту папку нельзя переместить");
        var dir = Resolve(destDir);
        if (!Directory.Exists(dir)) throw new FsException("Папка назначения не найдена");
        if (Directory.Exists(src) && Inside(src, dir)) throw new FsException("Нельзя переместить папку внутрь самой себя");
        var dst = FreeName(dir, Path.GetFileName(src));
        if (Directory.Exists(src)) Directory.Move(src, dst); else File.Move(src, dst);
        return dst;
    }
}
