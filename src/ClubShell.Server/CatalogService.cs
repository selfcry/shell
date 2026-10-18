using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

public record AppEntry(string Id, string Title, string Kind, string? Group, string? Cover, List<string> Paths, string? Args, string? WorkDir,
    bool ClubAccount, int? MinAge, bool Enabled, bool Pinned, int Sort);

public record BlockedEntry(long Id, string Process, string? Note);

/// <summary>
/// Каталог игр и приложений клуба и список запрещённых программ. Управляется из админ-панели, оболочки берут его с сервера.
/// Читается часто (каждая оболочка раз в минуту), поэтому лежит в памяти и сбрасывается при любой правке.
/// </summary>
public sealed class CatalogService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Db _db;
    private readonly string _coversDir;
    private readonly object _writeLock = new();
    private volatile List<AppEntry>? _apps;
    private volatile List<BlockedEntry>? _blocked;
    private long _version;

    public CatalogService(Db db, string dataDir)
    {
        _db = db;
        _coversDir = Path.Combine(dataDir, "covers");
        Directory.CreateDirectory(_coversDir);
        using var c = _db.Open();
        Db.Exec(c, """
            CREATE TABLE IF NOT EXISTS apps (
              id TEXT PRIMARY KEY, title TEXT NOT NULL, kind TEXT NOT NULL DEFAULT 'game', grp TEXT, cover TEXT,
              paths TEXT NOT NULL DEFAULT '[]', args TEXT, work_dir TEXT,
              club_account INTEGER NOT NULL DEFAULT 0, min_age INTEGER, enabled INTEGER NOT NULL DEFAULT 1,
              pinned INTEGER NOT NULL DEFAULT 0, sort INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS blocked (
              id INTEGER PRIMARY KEY AUTOINCREMENT, process TEXT NOT NULL UNIQUE COLLATE NOCASE, note TEXT);
            CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
            """);
        _version = long.TryParse(Db.Scalar<string>(c, "SELECT value FROM meta WHERE key = 'catalog_version'"), out var v) ? v : 0;
        if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM apps") == 0) SeedDefaults(c);
    }

    /// <summary>Меняется при любой правке каталога или запретов: оболочка сравнивает и перечитывает только при изменении.</summary>
    public long Version => Interlocked.Read(ref _version);

    private void Bump()
    {
        var v = Db.Now() * 1000 + Random.Shared.Next(0, 1000);
        Interlocked.Exchange(ref _version, v);
        _apps = null; _blocked = null;
        using var c = _db.Open();
        Db.Exec(c, "INSERT INTO meta(key, value) VALUES('catalog_version', $v) ON CONFLICT(key) DO UPDATE SET value = $v", ("$v", v.ToString()));
    }

    private static AppEntry Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
        JsonSerializer.Deserialize<List<string>>(r.GetString(5), Json) ?? new(), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
        r.GetInt32(8) == 1, r.IsDBNull(9) ? null : r.GetInt32(9), r.GetInt32(10) == 1, r.GetInt32(11) == 1, r.GetInt32(12));

    private const string Cols = "id, title, kind, grp, cover, paths, args, work_dir, club_account, min_age, enabled, pinned, sort";

    public List<AppEntry> Apps(bool onlyEnabled)
    {
        var all = _apps;
        if (all is null)
        {
            using var c = _db.Open();
            all = Db.Query(c, $"SELECT {Cols} FROM apps ORDER BY sort, title", Read);
            _apps = all;
        }
        return onlyEnabled ? all.Where(a => a.Enabled).ToList() : all;
    }

    public List<BlockedEntry> Blocked()
    {
        var all = _blocked;
        if (all is null)
        {
            using var c = _db.Open();
            all = Db.Query(c, "SELECT id, process, note FROM blocked ORDER BY process", r => new BlockedEntry(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
            _blocked = all;
        }
        return all;
    }

    // ---------- проверка и запись ----------

    private static string? Validate(AppEntry a)
    {
        if (string.IsNullOrWhiteSpace(a.Id) || !System.Text.RegularExpressions.Regex.IsMatch(a.Id, "^[a-z0-9][a-z0-9_-]{0,31}$")) return "Код: латиница, цифры, - и _, до 32 символов";
        if (string.IsNullOrWhiteSpace(a.Title) || a.Title.Length > 80) return "Название обязательно (до 80 символов)";
        if (a.Kind is not ("game" or "app")) return "Тип: игра или приложение";
        if (a.Paths.Count == 0 || a.Paths.Count > 3 || a.Paths.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 400)) return "Укажите от 1 до 3 путей запуска";
        if ((a.Args?.Length ?? 0) > 400) return "Слишком длинные аргументы";
        if (a.MinAge is < 0 or > 21) return "Возраст от 0 до 21";
        return null;
    }

    public Result Upsert(AppEntry a)
    {
        var bad = Validate(a);
        if (bad is not null) return new Result(false, bad);
        lock (_writeLock)
        {
            using var c = _db.Open();
            Db.Exec(c, $"""
                INSERT INTO apps({Cols}) VALUES($id, $t, $k, $g, NULL, $p, $a, $w, $ca, $ma, $e, $pi, $s)
                ON CONFLICT(id) DO UPDATE SET title=$t, kind=$k, grp=$g, paths=$p, args=$a, work_dir=$w, club_account=$ca, min_age=$ma, enabled=$e, pinned=$pi, sort=$s
                """,
                ("$id", a.Id), ("$t", a.Title.Trim()), ("$k", a.Kind), ("$g", string.IsNullOrWhiteSpace(a.Group) ? null : a.Group.Trim()),
                ("$p", JsonSerializer.Serialize(a.Paths.Select(p => p.Trim()), Json)), ("$a", string.IsNullOrWhiteSpace(a.Args) ? null : a.Args.Trim()),
                ("$w", string.IsNullOrWhiteSpace(a.WorkDir) ? null : a.WorkDir.Trim()), ("$ca", a.ClubAccount ? 1 : 0), ("$ma", a.MinAge),
                ("$e", a.Enabled ? 1 : 0), ("$pi", a.Pinned ? 1 : 0), ("$s", a.Sort));
        }
        Bump();
        return new Result(true);
    }

    public bool Delete(string id)
    {
        string? cover;
        lock (_writeLock)
        {
            using var c = _db.Open();
            cover = Db.Scalar<string>(c, "SELECT cover FROM apps WHERE id = $i", ("$i", id));
            if (Db.Exec(c, "DELETE FROM apps WHERE id = $i", ("$i", id)) == 0) return false;
        }
        DeleteCoverFile(cover);
        Bump();
        return true;
    }

    // ---------- обложки ----------

    private static string? ExtensionByMagic(byte[] b)
    {
        if (b.Length > 12 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length > 12 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return "webp";
        return null;
    }

    /// <summary>Сохраняет обложку (jpg/png/webp до 2 МБ; тип определяется по содержимому, а не по имени).</summary>
    public Result SetCover(string id, byte[] data)
    {
        if (data.Length == 0 || data.Length > 2_000_000) return new Result(false, "Обложка: до 2 МБ");
        var ext = ExtensionByMagic(data);
        if (ext is null) return new Result(false, "Нужна картинка JPG, PNG или WEBP");
        string? old;
        var name = $"{id}-{Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}.{ext}";
        lock (_writeLock)
        {
            using var c = _db.Open();
            old = Db.Scalar<string>(c, "SELECT cover FROM apps WHERE id = $i", ("$i", id));
            if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM apps WHERE id = $i", ("$i", id)) == 0) return new Result(false, "Игра не найдена");
            File.WriteAllBytes(Path.Combine(_coversDir, name), data);
            Db.Exec(c, "UPDATE apps SET cover = $c WHERE id = $i", ("$c", name), ("$i", id));
        }
        DeleteCoverFile(old);
        Bump();
        return new Result(true, name);
    }

    public bool ClearCover(string id)
    {
        string? old;
        lock (_writeLock)
        {
            using var c = _db.Open();
            old = Db.Scalar<string>(c, "SELECT cover FROM apps WHERE id = $i", ("$i", id));
            if (Db.Exec(c, "UPDATE apps SET cover = NULL WHERE id = $i", ("$i", id)) == 0) return false;
        }
        DeleteCoverFile(old);
        Bump();
        return true;
    }

    private void DeleteCoverFile(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('\\') || name.Contains("..")) return;
        try { File.Delete(Path.Combine(_coversDir, name)); } catch (IOException) { /* не страшно */ }
    }

    public string? CoverPath(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9._-]{1,80}$") || name.Contains("..")) return null;
        var p = Path.Combine(_coversDir, name);
        return File.Exists(p) ? p : null;
    }

    // ---------- запреты ----------

    public Result AddBlocked(string process, string? note)
    {
        process = (process ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(process, @"^[A-Za-z0-9 ._()\-+]{1,64}\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return new Result(false, "Имя процесса вида program.exe");
        lock (_writeLock)
        {
            using var c = _db.Open();
            try { Db.Exec(c, "INSERT INTO blocked(process, note) VALUES($p, $n)", ("$p", process), ("$n", string.IsNullOrWhiteSpace(note) ? null : note.Trim())); }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return new Result(false, "Уже в списке"); }
        }
        Bump();
        return new Result(true);
    }

    public bool RemoveBlocked(long id)
    {
        lock (_writeLock)
        {
            using var c = _db.Open();
            if (Db.Exec(c, "DELETE FROM blocked WHERE id = $i", ("$i", id)) == 0) return false;
        }
        Bump();
        return true;
    }

    // ---------- начальный каталог ----------

    private static AppEntry E(string id, string title, string kind, string? group, string[] paths, string? args = null, bool club = false, int? age = null, bool pinned = false, int sort = 0) =>
        new(id, title, kind, group, null, paths.ToList(), args, null, club, age, true, pinned, sort);

    private void SeedDefaults(SqliteConnection c)
    {
        const string rental = "-idgame {id} -autostart -user {user}";
        var items = new[]
        {
            E("cs2", "Counter-Strike 2", "game", "Шутеры", new[] { "find:RentalGames.exe" }, rental.Replace("{id}", "730"), club: true, sort: 10),
            E("valorant", "VALORANT", "game", "Шутеры", new[] { @"C:\Riot Games\Riot Client\RiotClientServices.exe" }, "--launch-product=valorant --launch-patchline=live", age: 16, sort: 20),
            E("pubg", "PUBG: BATTLEGROUNDS", "game", "Шутеры", new[] { "find:RentalGames.exe" }, rental.Replace("{id}", "578080"), club: true, sort: 30),
            E("dota2", "Dota 2", "game", "MOBA", new[] { "steam://rungameid/570" }, sort: 40),
            E("lol", "League of Legends", "game", "MOBA", new[] { @"C:\Riot Games\Riot Client\RiotClientServices.exe" }, "--launch-product=league_of_legends --launch-patchline=live", sort: 50),
            E("gta5", "Grand Theft Auto V", "game", "Экшен", new[] { "find:RentalGames.exe" }, rental.Replace("{id}", "271590"), club: true, age: 18, sort: 60),
            E("fortnite", "Fortnite", "game", "Шутеры", new[] { "com.epicgames.launcher://apps/fn%3A4fe75bbc5a674f4f9b356b5c90567da5%3AFortnite?action=launch&silent=true" }, sort: 70),
            E("minecraft", "Minecraft", "game", "Экшен", new[] { "shell:AppsFolder/Microsoft.4297127D64EC6_8wekyb3d8bbwe!Minecraft" }, sort: 80),
            E("stalker", "S.T.A.L.K.E.R.: Shadow of Chornobyl", "game", "Экшен", new[] { "steam://rungameid/4500" }, age: 18, sort: 90),

            E("browser", "Браузер", "app", "Интернет", new[] { "find:chrome.exe", "find:msedge.exe", "find:firefox.exe" }, pinned: true, sort: 10),
            E("discord", "Discord", "app", "Общение", new[] { @"%LOCALAPPDATA%\Discord\Update.exe" }, "--processStart Discord.exe", pinned: true, sort: 20),
            E("telegram", "Telegram", "app", "Общение", new[] { @"%APPDATA%\Telegram Desktop\Telegram.exe", "find:Telegram.exe" }, pinned: true, sort: 30),
            E("steam", "Steam", "app", "Лаунчеры", new[] { @"C:\Program Files (x86)\Steam\Steam.exe", "find:Steam.exe" }, sort: 40),
            E("epic", "Epic Games", "app", "Лаунчеры", new[] { @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe" }, sort: 50),
            E("battlenet", "Battle.net", "app", "Лаунчеры", new[] { @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe" }, sort: 60),
        };
        foreach (var a in items)
            Db.Exec(c, $"INSERT INTO apps({Cols}) VALUES($id, $t, $k, $g, NULL, $p, $a, NULL, $ca, $ma, 1, $pi, $s)",
                ("$id", a.Id), ("$t", a.Title), ("$k", a.Kind), ("$g", a.Group), ("$p", JsonSerializer.Serialize(a.Paths, Json)), ("$a", a.Args),
                ("$ca", a.ClubAccount ? 1 : 0), ("$ma", a.MinAge), ("$pi", a.Pinned ? 1 : 0), ("$s", a.Sort));
    }
}
