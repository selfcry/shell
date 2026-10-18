using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

/// <summary>SQLite: схема и небольшие помощники. Время везде — секунды Unix (UTC).</summary>
public sealed class Db
{
    private readonly string _connStr;

    public Db(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connStr = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true, ForeignKeys = true }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        using var cmd = c.CreateCommand();
        // На каждое соединение: ожидание блокировки, быстрые записи без потери целостности (WAL + NORMAL), кэш и временные данные в памяти.
        cmd.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA synchronous = NORMAL; PRAGMA cache_size = -32000; PRAGMA temp_store = MEMORY; PRAGMA mmap_size = 134217728;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void Init()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode = WAL;");
        Exec(c, """
            CREATE TABLE IF NOT EXISTS guests (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              login TEXT NOT NULL UNIQUE COLLATE NOCASE,
              pass TEXT NOT NULL,
              balance INTEGER NOT NULL DEFAULT 0,
              birth TEXT,
              banned INTEGER NOT NULL DEFAULT 0,
              created INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS tariffs (
              id TEXT PRIMARY KEY,
              name TEXT NOT NULL,
              minutes INTEGER NOT NULL,
              price INTEGER NOT NULL,
              night_only INTEGER NOT NULL DEFAULT 0,
              zone TEXT NOT NULL DEFAULT 'ARENA',
              active INTEGER NOT NULL DEFAULT 1,
              sort INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS pcs (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              name TEXT NOT NULL UNIQUE COLLATE NOCASE,
              zone TEXT NOT NULL DEFAULT 'ARENA',
              token_hash TEXT NOT NULL UNIQUE,
              last_seen INTEGER NOT NULL DEFAULT 0,
              created INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              guest_id INTEGER NOT NULL REFERENCES guests(id),
              pc_id INTEGER NOT NULL REFERENCES pcs(id),
              tariff_id TEXT NOT NULL,
              started INTEGER NOT NULL,
              ends INTEGER NOT NULL,
              ended INTEGER,
              price INTEGER NOT NULL,
              reason TEXT);
            CREATE INDEX IF NOT EXISTS ix_sessions_active ON sessions(pc_id) WHERE ended IS NULL;
            CREATE INDEX IF NOT EXISTS ix_sessions_guest ON sessions(guest_id, id);
            CREATE TABLE IF NOT EXISTS tx (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              guest_id INTEGER NOT NULL REFERENCES guests(id),
              amount INTEGER NOT NULL,
              kind TEXT NOT NULL,
              note TEXT,
              session_id INTEGER,
              ts INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_tx_guest ON tx(guest_id, id);
            CREATE INDEX IF NOT EXISTS ix_tx_kind_ts ON tx(kind, ts);
            """);

        // Миграция: телефон гостя (подтверждается SMS-кодом при регистрации), уникален.
        var hasPhone = Query(c, "PRAGMA table_info(guests)", r => r.GetString(1)).Contains("phone");
        if (!hasPhone) Exec(c, "ALTER TABLE guests ADD COLUMN phone TEXT");
        Exec(c, "CREATE UNIQUE INDEX IF NOT EXISTS ix_guests_phone ON guests(phone) WHERE phone IS NOT NULL");
    }

    public static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static int Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Make(c, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static T? Scalar<T>(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = Make(c, sql, args);
        var v = cmd.ExecuteScalar();
        if (v is null || v is DBNull) return default;
        return (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var cmd = Make(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static T? QueryOne<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        var list = Query(c, sql, map, args);
        return list.Count > 0 ? list[0] : default;
    }

    public static long LastId(SqliteConnection c) => Scalar<long>(c, "SELECT last_insert_rowid()");

    private static SqliteCommand Make(SqliteConnection c, string sql, (string, object?)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
}
