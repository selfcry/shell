using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

/// <summary>Кто работает в админке: владелец (ключ из admin-key.txt) или сотрудник со своей учёткой.</summary>
public sealed record Actor(string Name, string Role, long? OperatorId)
{
    public bool IsOwner => Role == "owner";
}

public sealed record OperatorRow(long Id, string Login, string Name, string Role, bool Active, long Created);

/// <summary>
/// Учётки сотрудников: вход логином и паролем выдаёт токен на 12 часов (в памяти, на сервере только его хеш).
/// Роли: cashier — касса, гости, зал, брони, бар; owner — всё, включая тарифы, каталог, аналитику, журнал и сотрудников.
/// </summary>
public sealed class Operators
{
    private readonly Db _db;
    private readonly ConcurrentDictionary<string, (Actor actor, long expires)> _tokens = new(); // sha256(токен) -> сотрудник
    private readonly ConcurrentDictionary<string, (int fails, long until)> _fails = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _hashSlots = new(2);
    private const int TokenTtl = 12 * 3600;

    public Operators(Db db) => _db = db;

    private static OperatorRow Read(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) == 1, r.GetInt64(5));

    public List<OperatorRow> List()
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, login, name, role, active, created FROM operators ORDER BY login", Read);
    }

    public Result Create(string login, string name, string password, string role)
    {
        login = (login ?? "").Trim();
        if (login.Length is < 3 or > 24 || !login.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.' or '-')) return new Result(false, "Логин: 3–24 символа, буквы, цифры, . _ -");
        if (password is null || password.Length is < 8 or > 128) return new Result(false, "Пароль сотрудника — от 8 символов");
        if (role is not ("cashier" or "owner")) return new Result(false, "Роль: кассир или владелец");
        name = string.IsNullOrWhiteSpace(name) ? login : name.Trim()[..Math.Min(name.Trim().Length, 40)];
        using var c = _db.Open();
        try
        {
            Db.Exec(c, "INSERT INTO operators(login, name, pass, role, active, created) VALUES($l, $n, $p, $r, 1, $t)",
                ("$l", login), ("$n", name), ("$p", Security.HashPassword(password)), ("$r", role), ("$t", Db.Now()));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return new Result(false, "Такой логин уже есть"); }
        return new Result(true);
    }

    public Result Update(long id, string? password, bool? active, string? role, string? name)
    {
        if (password is not null && password.Length is < 8 or > 128) return new Result(false, "Пароль сотрудника — от 8 символов");
        if (role is not null and not ("cashier" or "owner")) return new Result(false, "Роль: кассир или владелец");
        using var c = _db.Open();
        if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM operators WHERE id = $i", ("$i", id)) == 0) return new Result(false, "Сотрудник не найден");
        if (password is not null) Db.Exec(c, "UPDATE operators SET pass = $p WHERE id = $i", ("$p", Security.HashPassword(password)), ("$i", id));
        if (active is not null) Db.Exec(c, "UPDATE operators SET active = $a WHERE id = $i", ("$a", active.Value ? 1 : 0), ("$i", id));
        if (role is not null) Db.Exec(c, "UPDATE operators SET role = $r WHERE id = $i", ("$r", role), ("$i", id));
        if (!string.IsNullOrWhiteSpace(name)) Db.Exec(c, "UPDATE operators SET name = $n WHERE id = $i", ("$n", name.Trim()[..Math.Min(name.Trim().Length, 40)]), ("$i", id));
        // смена пароля, роли или отключение — выходят все его сеансы
        if (password is not null || active == false || role is not null)
            foreach (var kv in _tokens.Where(kv => kv.Value.actor.OperatorId == id).ToList()) _tokens.TryRemove(kv.Key, out _);
        return new Result(true);
    }

    /// <summary>Вход сотрудника. Ошибка — одним текстом, чтобы не подсказывать, существует ли логин.</summary>
    public async Task<(string? token, Actor? actor, string? error)> LoginAsync(string login, string password, string? ip)
    {
        var key = (login ?? "").Trim().ToLowerInvariant();
        var ipKey = "ip:" + (ip ?? "?");
        var now = Db.Now();
        foreach (var k in new[] { key, ipKey })
            if (_fails.TryGetValue(k, out var f) && f.until > now) return (null, null, $"Слишком много попыток, подождите {f.until - now} с");
        if (key.Length is 0 or > 24 || password is null || password.Length > 128) return (null, null, "Неверный логин или пароль");

        OperatorRow? op; string? pass;
        using (var c = _db.Open())
        {
            var row = Db.QueryOne(c, "SELECT id, login, name, role, active, created, pass FROM operators WHERE login = $l COLLATE NOCASE",
                r => (op: Read(r), pass: r.GetString(6)), ("$l", login!.Trim()));
            (op, pass) = (row.op, row.pass);
        }
        if (!await _hashSlots.WaitAsync(TimeSpan.FromSeconds(3))) return (null, null, "Сервер занят, попробуйте ещё раз");
        bool ok;
        try { ok = op is not null && pass is not null && Security.VerifyPassword(password, pass); }
        finally { _hashSlots.Release(); }
        if (!ok || !op!.Active)
        {
            foreach (var k in new[] { key, ipKey })
            {
                var f = _fails.GetValueOrDefault(k);
                _fails[k] = f.fails + 1 >= 5 ? (0, now + 300) : (f.fails + 1, 0); // 5 ошибок подряд — 5 минут паузы
            }
            return (null, null, "Неверный логин или пароль");
        }
        _fails.TryRemove(key, out _);
        var token = "op_" + Security.NewToken();
        var actor = new Actor(op.Name, op.Role, op.Id);
        _tokens[Security.Sha256(token)] = (actor, now + TokenTtl);
        foreach (var kv in _tokens) if (kv.Value.expires < now) _tokens.TryRemove(kv.Key, out _);
        return (token, actor, null);
    }

    public Actor? ByToken(string token)
    {
        if (!token.StartsWith("op_", StringComparison.Ordinal)) return null;
        return _tokens.TryGetValue(Security.Sha256(token), out var t) && t.expires > Db.Now() ? t.actor : null;
    }

    public void Logout(string token) => _tokens.TryRemove(Security.Sha256(token), out _);

    /// <summary>Что кассиру нельзя: настройка клуба, деньги «из воздуха» и чужие данные. Остальное — можно.</summary>
    public static bool OwnerOnly(string method, string path)
    {
        var get = HttpMethods.IsGet(method);
        string P(string s) => "/api/admin" + s;
        if (path.StartsWith(P("/operators")) || path.StartsWith(P("/settings")) || path.StartsWith(P("/audit")) || path.StartsWith(P("/analytics"))) return true;
        if (get) return false;
        foreach (var prefix in new[] { "/apps", "/blocked", "/tariffs", "/levels", "/promos" })
            if (path.StartsWith(P(prefix))) return true;
        if (path == P("/pcs") && HttpMethods.IsPost(method)) return true;                         // добавить ПК
        if (path.StartsWith(P("/pcs/")) && (HttpMethods.IsDelete(method) || path.EndsWith("/token"))) return true; // удалить ПК, новый токен
        if (path.StartsWith(P("/guests/")) && path.EndsWith("/bonus")) return true;               // бонусы вручную
        if (path == P("/products") && HttpMethods.IsPut(method)) return true;                     // цены и товары; приход на склад — можно
        return false;
    }
}
