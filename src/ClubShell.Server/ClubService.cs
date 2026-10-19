using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

public sealed record Result(bool Ok, string? Error = null);

/// <summary>Вся бизнес-логика клуба: гости, баланс, тарифы, ПК, сессии. Сервер — единственный источник правды о времени и деньгах.</summary>
public sealed class ClubService
{
    private readonly Db _db;
    private readonly TimeZoneInfo _tz;
    private readonly ConcurrentDictionary<string, (int fails, long until)> _loginFails = new(StringComparer.Ordinal); // ключ — нормализованный логин
    private const int MaxBalance = 10_000_000;

    /// <summary>Логин для сравнения и счётчиков: без пробелов по краям и в нижнем регистре (в том числе кириллица).</summary>
    private static string Norm(string? login) => (login ?? "").Trim().ToLowerInvariant();

    // ---- Кэши в памяти: горячие запросы (пульс ПК, проверка токена, тарифы, активные сеансы) не ходят в базу ----
    private readonly object _writeLock = new();                                  // запись в SQLite идёт строго по одной
    private readonly SemaphoreSlim _hashSlots = new(4);                          // не больше 4 одновременных PBKDF2, иначе вход «положит» процессор
    private readonly ConcurrentDictionary<long, SessionInfo> _active = new();    // pcId -> активный сеанс
    private readonly ConcurrentDictionary<long, long> _seen = new();             // pcId -> последний пульс (секунды Unix)
    private readonly ConcurrentDictionary<long, byte> _seenDirty = new();        // ПК, чей last_seen ещё не записан в базу
    private readonly ConcurrentDictionary<string, Pc> _pcByHash = new();         // sha256(токен) -> ПК
    private volatile List<Tariff>? _tariffCache;
    private readonly ConcurrentDictionary<long, long> _guestPc = new();         // гость -> ПК, на котором он последний раз входил (чтобы прислать новый баланс)
    // Кто вошёл на ПК (по паролю) и до какого времени может покупать сеанс. Без этого любой, у кого есть токен ПК,
    // мог бы запустить сеанс за счёт чужого логина, не зная пароля.
    private readonly ConcurrentDictionary<long, (long guestId, long until)> _loggedIn = new();
    private readonly ConcurrentDictionary<long, Queue<long>> _pcFails = new();  // ПК -> время неудачных входов (перебор с одного ПК)
    private int _tariffGen, _pcGen;                                              // поколения кэшей: устаревшее чтение не попадает в кэш

    public ClubService(Db db, TimeZoneInfo tz)
    {
        (_db, _tz) = (db, tz);
        using var c = _db.Open();
        foreach (var s in Db.Query(c, SessionSelect + " WHERE s.ended IS NULL", ReadSession)) _active[s.PcId] = s;
    }

    public long LastSeen(long pcId) => _seen.GetValueOrDefault(pcId);

    /// <summary>Записывает накопленные пульсы одной транзакцией (раз в ~30 с вместо записи на каждый пульс).</summary>
    public int FlushPresence()
    {
        var ids = _seenDirty.Keys.ToList();
        if (ids.Count == 0) return 0;
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            foreach (var id in ids)
            {
                _seenDirty.TryRemove(id, out _);
                Db.Exec(c, "UPDATE pcs SET last_seen = $t WHERE id = $i", ("$t", _seen.GetValueOrDefault(id)), ("$i", id));
            }
            tx.Commit();
        }
        return ids.Count;
    }

    // ---------- вспомогательное ----------

    public bool IsNight(DateTimeOffset utc)
    {
        var h = TimeZoneInfo.ConvertTime(utc, _tz).Hour;
        return h >= 22 || h < 8;
    }

    private static string? Validate(string login, string? password)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Length > 32 || login.Any(char.IsControl)) return "Недопустимый логин";
        if (password is not null && (password.Length < 6 || password.Length > 128)) return "Пароль должен быть от 6 до 128 символов";
        return null;
    }

    private static Pc ReadPc(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3));
    private static Tariff ReadTariff(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4) == 1, r.GetString(5), r.GetInt32(6) == 1, r.GetInt32(7));
    private static Guest ReadGuest(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4) == 1, r.GetInt64(5));

    private const string SessionSelect = """
        SELECT s.id, s.guest_id, g.login, s.pc_id, p.name, s.tariff_id, s.started, s.ends, s.ended, s.price, s.reason
        FROM sessions s JOIN guests g ON g.id = s.guest_id JOIN pcs p ON p.id = s.pc_id
        """;
    private static SessionInfo ReadSession(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4),
        r.GetString(5), r.GetInt64(6), r.GetInt64(7), r.IsDBNull(8) ? null : r.GetInt64(8), r.GetInt32(9), r.IsDBNull(10) ? null : r.GetString(10));

    // ---------- ПК ----------

    public Pc? PcByToken(string token)
    {
        var hash = Security.Sha256(token);
        if (_pcByHash.TryGetValue(hash, out var cached)) return cached;
        var gen = Volatile.Read(ref _pcGen);
        using var c = _db.Open();
        var pc = Db.QueryOne(c, "SELECT id, name, zone, last_seen FROM pcs WHERE token_hash = $h AND deleted = 0", ReadPc, ("$h", hash));
        if (pc is not null && gen == Volatile.Read(ref _pcGen)) _pcByHash[hash] = pc;
        return pc;
    }

    public (Pc? pc, string? token, string? error) CreatePc(string name, string? zone)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 32) return (null, null, "Недопустимое имя ПК");
        var token = Security.NewToken();
        using var c = _db.Open();
        try
        {
            lock (_writeLock)
            Db.Exec(c, "INSERT INTO pcs(name, zone, token_hash, created) VALUES($n, $z, $h, $t)",
                ("$n", name.Trim()), ("$z", string.IsNullOrWhiteSpace(zone) ? "ARENA" : zone.Trim()), ("$h", Security.Sha256(token)), ("$t", Db.Now()));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19) { return (null, null, "ПК с таким именем уже есть"); }
        var id = Db.LastId(c);
        return (new Pc(id, name.Trim(), string.IsNullOrWhiteSpace(zone) ? "ARENA" : zone.Trim(), 0), token, null);
    }

    /// <summary>Новый токен для ПК (старый сразу перестаёт работать). null — ПК не найден.</summary>
    public string? ResetPcToken(long id)
    {
        var token = Security.NewToken();
        using var c = _db.Open();
        bool done;
        lock (_writeLock) done = Db.Exec(c, "UPDATE pcs SET token_hash = $h WHERE id = $i", ("$h", Security.Sha256(token)), ("$i", id)) > 0;
        if (!done) return null;
        ForgetPc(id);
        return token;
    }

    private void ForgetPc(long id)
    {
        Interlocked.Increment(ref _pcGen);
        foreach (var kv in _pcByHash.Where(kv => kv.Value.Id == id).ToList()) _pcByHash.TryRemove(kv.Key, out _);
        _loggedIn.TryRemove(id, out _);
    }

    public List<Pc> Pcs()
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, name, zone, last_seen FROM pcs WHERE deleted = 0 ORDER BY name", ReadPc)
            .Select(p => p with { LastSeen = Math.Max(p.LastSeen, LastSeen(p.Id)) }).ToList();
    }

    public bool DeletePc(long id)
    {
        using var c = _db.Open();
        if (_active.ContainsKey(id)) return false;
        bool done;
        // Не удаляем строку: на ПК ссылаются прошлые сеансы. Токен заменяется случайным, имя освобождается.
        lock (_writeLock) done = Db.Exec(c, "UPDATE pcs SET deleted = 1, token_hash = $h, name = name || ' #' || id || ' (удалён)' WHERE id = $i AND deleted = 0",
            ("$h", "deleted:" + Security.NewToken()), ("$i", id)) > 0;
        if (done)
        {
            ForgetPc(id);
            _seen.TryRemove(id, out _);
            _seenDirty.TryRemove(id, out _);
        }
        return done;
    }

    /// <summary>Пульс ПК: только память, в базу попадёт при следующем FlushPresence().</summary>
    public void Touch(long pcId)
    {
        _seen[pcId] = Db.Now();
        _seenDirty[pcId] = 0;
    }

    // ---------- гости ----------

    public Result CreateGuest(string login, string password, int balance, string? birth)
    {
        if (password is null) return new Result(false, "Нужен пароль");
        var bad = Validate(login, password);
        if (bad is not null) return new Result(false, bad);
        if (balance < 0 || balance > MaxBalance) return new Result(false, "Недопустимый начальный баланс");
        var hash = Security.HashPassword(password);
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            try
            {
                Db.Exec(c, "INSERT INTO guests(login, pass, balance, birth, created) VALUES($l, $p, $b, $d, $t)",
                    ("$l", login.Trim()), ("$p", hash), ("$b", balance), ("$d", birth), ("$t", Db.Now()));
                if (balance > 0)
                    Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts) VALUES($g, $a, 'topup', 'Начальный баланс', $t)",
                        ("$g", Db.LastId(c)), ("$a", balance), ("$t", Db.Now()));
                tx.Commit();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19) { return new Result(false, "Гость с таким логином уже есть"); }
        }
        return new Result(true);
    }

    public List<Guest> Guests(string? search)
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE $q = '' OR login LIKE '%' || $q || '%' ORDER BY login LIMIT 500",
            ReadGuest, ("$q", search?.Trim() ?? ""));
    }

    public Result UpdateGuest(long id, string? password, bool? banned, string? birth)
    {
        if (password is not null && Validate("x", password) is { } bad) return new Result(false, bad);
        using var c = _db.Open();
        lock (_writeLock) {
        if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM guests WHERE id = $i", ("$i", id)) == 0) return new Result(false, "Гость не найден");
        if (password is not null) Db.Exec(c, "UPDATE guests SET pass = $p WHERE id = $i", ("$p", Security.HashPassword(password)), ("$i", id));
        if (banned is not null) Db.Exec(c, "UPDATE guests SET banned = $b WHERE id = $i", ("$b", banned.Value ? 1 : 0), ("$i", id));
        if (birth is not null) Db.Exec(c, "UPDATE guests SET birth = $d WHERE id = $i", ("$d", birth.Length == 0 ? null : birth), ("$i", id));
        }
        return new Result(true);
    }

    /// <summary>Пополнение (amount &gt; 0) или списание (&lt; 0). Баланс не уходит в минус.</summary>
    private readonly ConcurrentDictionary<string, (long ts, int balance)> _topupDone = new();

    public (Result res, int balance) TopUp(long guestId, int amount, string? note, string? requestId = null)
    {
        if (amount == 0 || Math.Abs(amount) > 1_000_000) return (new Result(false, "Недопустимая сумма"), 0);
        if (note is { Length: > 200 }) note = note[..200];
        lock (_writeLock)
        {
        var rid = string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 ? null : $"{guestId}:{requestId}";
        if (rid is not null && _topupDone.TryGetValue(rid, out var done)) return (new Result(true, "duplicate"), done.balance); // уже проведено
        foreach (var kv in _topupDone) if (Db.Now() - kv.Value.ts > 600) _topupDone.TryRemove(kv.Key, out _);
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var bal = Db.Scalar<int?>(c, "SELECT balance FROM guests WHERE id = $i", ("$i", guestId));
        if (bal is null) return (new Result(false, "Гость не найден"), 0);
        if (bal + amount < 0) return (new Result(false, "Недостаточно средств для списания"), bal.Value);
        if ((long)bal + amount > MaxBalance) return (new Result(false, "Слишком большой баланс"), bal.Value);
        Db.Exec(c, "UPDATE guests SET balance = balance + $a WHERE id = $i", ("$a", amount), ("$i", guestId));
        Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts) VALUES($g, $a, $k, $n, $t)",
            ("$g", guestId), ("$a", amount), ("$k", amount > 0 ? "topup" : "charge"), ("$n", note), ("$t", Db.Now()));
        tx.Commit();
        if (rid is not null) _topupDone[rid] = (Db.Now(), bal.Value + amount);
        return (new Result(true), bal.Value + amount);
        }
    }

    /// <summary>ПК, за которым гость сейчас (или последний раз) работал, и его актуальный баланс: чтобы прислать оболочке обновление.</summary>
    public (long pcId, string login, int balance)? BalanceTarget(long guestId)
    {
        if (!_guestPc.TryGetValue(guestId, out var pcId)) return null;
        // гость уже ушёл: за ПК может сидеть другой, ему чужой логин и баланс показывать нельзя
        var here = _active.TryGetValue(pcId, out var act) ? act.GuestId == guestId
                 : _loggedIn.TryGetValue(pcId, out var li) && li.guestId == guestId && li.until > Db.Now();
        if (!here) return null;
        using var c = _db.Open();
        var g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE id = $i", ReadGuest, ("$i", guestId));
        return g is null ? null : (pcId, g.Login, g.Balance);
    }

    public List<object> Transactions(long guestId)
    {
        using var c = _db.Open();
        return Db.Query<object>(c, "SELECT id, amount, kind, note, ts FROM tx WHERE guest_id = $g ORDER BY id DESC LIMIT 200",
            r => new { id = r.GetInt64(0), amount = r.GetInt32(1), kind = r.GetString(2), note = r.IsDBNull(3) ? null : r.GetString(3), ts = r.GetInt64(4) }, ("$g", guestId));
    }

    // ---------- тарифы ----------

    public List<Tariff> Tariffs(string? zone = null, bool onlyActive = true)
    {
        var all = _tariffCache;
        if (all is null)
        {
            var gen = Volatile.Read(ref _tariffGen);
            using var c = _db.Open();
            all = Db.Query(c, "SELECT id, name, minutes, price, night_only, zone, active, sort FROM tariffs ORDER BY sort, minutes", ReadTariff);
            if (gen == Volatile.Read(ref _tariffGen)) _tariffCache = all;
        }
        return all.Where(t => (zone is null || t.Zone.Equals(zone, StringComparison.OrdinalIgnoreCase)) && (!onlyActive || t.Active)).ToList();
    }

    public Result UpsertTariff(TariffReq t)
    {
        if (string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 32 || string.IsNullOrWhiteSpace(t.Name)) return new Result(false, "Нужны id и название");
        if (t.Minutes is < 1 or > 24 * 60 * 7 || t.Price < 0) return new Result(false, "Недопустимые время или цена");
        using var c = _db.Open();
        lock (_writeLock)
        Db.Exec(c, """
            INSERT INTO tariffs(id, name, minutes, price, night_only, zone, active, sort) VALUES($i, $n, $m, $p, $o, $z, $a, $s)
            ON CONFLICT(id) DO UPDATE SET name = $n, minutes = $m, price = $p, night_only = $o, zone = $z, active = $a, sort = $s
            """, ("$i", t.Id.Trim()), ("$n", t.Name.Trim()), ("$m", t.Minutes), ("$p", t.Price), ("$o", t.NightOnly ? 1 : 0),
            ("$z", string.IsNullOrWhiteSpace(t.Zone) ? "ARENA" : t.Zone.Trim()), ("$a", t.Active == false ? 0 : 1), ("$s", t.Sort ?? 0));
        Interlocked.Increment(ref _tariffGen);
        _tariffCache = null;
        return new Result(true);
    }

    public bool DeleteTariff(string id)
    {
        using var c = _db.Open();
        bool done;
        lock (_writeLock) done = Db.Exec(c, "UPDATE tariffs SET active = 0 WHERE id = $i", ("$i", id)) > 0; // не удаляем: на него ссылаются прошлые сессии
        Interlocked.Increment(ref _tariffGen);
        _tariffCache = null;
        return done;
    }

    // ---------- вход и сессии ----------

    private bool IsLocked(string key, out int waitSec)
    {
        waitSec = 0;
        if (_loginFails.TryGetValue(key, out var f) && f.until > Db.Now()) { waitSec = (int)(f.until - Db.Now()); return true; }
        return false;
    }

    /// <summary>Не больше 20 неудачных входов за 10 минут с одного ПК (перебор паролей по многим логинам).</summary>
    private bool PcFailLimited(long pcId, bool record)
    {
        var now = Db.Now();
        var q = _pcFails.GetOrAdd(pcId, _ => new Queue<long>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > 600) q.Dequeue();
            if (record) q.Enqueue(now);
            return q.Count >= 20;
        }
    }

    /// <summary>Чистит устаревшие счётчики входов (вызывается фоновым обслуживанием), чтобы словари не росли бесконечно.</summary>
    public void PruneLoginState()
    {
        var now = Db.Now();
        var tooMany = _loginFails.Count > 20_000;
        foreach (var kv in _loginFails)
            if (kv.Value.until != 0 && kv.Value.until < now || kv.Value.until == 0 && tooMany) _loginFails.TryRemove(kv.Key, out _);
        foreach (var kv in _loggedIn) if (kv.Value.until < now) _loggedIn.TryRemove(kv.Key, out _);
    }

    /// <summary>Гость вошёл на ПК: только он может купить сеанс на этом ПК в ближайшие 30 минут (или пока идёт его сеанс).</summary>
    private void BindLogin(long pcId, long guestId, long until) => _loggedIn[pcId] = (guestId, until);

    /// <summary>Гость вышел (или его выкинули): покупать время на этом ПК больше нельзя.</summary>
    public void Logout(long pcId) => _loggedIn.TryRemove(pcId, out _);

    public (bool ok, string? error, Guest? guest, List<Tariff>? tariffs) Login(Pc pc, string login, string password)
    {
        var key = Norm(login);
        if (key.Length == 0 || key.Length > 32 || password is null || password.Length > 128) return (false, "Неверный логин или пароль", null, null);
        if (IsLocked(key, out var wait)) return (false, $"Слишком много попыток, подождите {wait} с", null, null);
        if (PcFailLimited(pc.Id, record: false)) return (false, "Слишком много неудачных входов с этого ПК, подождите 10 минут", null, null);

        using var c = _db.Open();
        var row = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created, pass FROM guests WHERE login = $l",
            r => (g: ReadGuest(r), pass: r.GetString(6)), ("$l", login.Trim()));
        bool passOk;
        if (!_hashSlots.Wait(TimeSpan.FromSeconds(3))) return (false, "Сервер занят, попробуйте ещё раз", null, null);
        try { passOk = row.g is not null && Security.VerifyPassword(password, row.pass); }
        finally { _hashSlots.Release(); }
        if (!passOk)
        {
            PcFailLimited(pc.Id, record: true);
            var f = _loginFails.GetValueOrDefault(key);
            var n = f.fails + 1;
            _loginFails[key] = n >= 5 ? (0, Db.Now() + 120) : (n, 0); // 5 неудач подряд — пауза 2 минуты
            return (false, "Неверный логин или пароль", null, null);
        }
        _loginFails.TryRemove(key, out _);
        if (row.g!.Banned) return (false, "Учётная запись заблокирована", null, null);
        _guestPc[row.g.Id] = pc.Id;
        BindLogin(pc.Id, row.g.Id, Db.Now() + 30 * 60);
        return (true, null, row.g, Tariffs(pc.Zone));
    }

    public (bool ok, string? error, int balance, int minutes) StartSession(Pc pc, string login, string tariffId)
    {
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            var g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE login = $l", ReadGuest, ("$l", (login ?? "").Trim()));
            // покупать может только тот, кто вошёл на этом ПК по паролю
            if (g is null || !_loggedIn.TryGetValue(pc.Id, out var li) || li.guestId != g.Id || li.until < Db.Now())
                return (false, "Войдите заново", 0, 0);
            if (g.Banned) return (false, "Учётная запись заблокирована", 0, 0);
            var t = Tariffs(pc.Zone).FirstOrDefault(x => x.Id == tariffId);
            if (t is null) return (false, "Тариф не найден", g.Balance, 0);
            if (t.NightOnly && !IsNight(DateTimeOffset.UtcNow)) return (false, "Ночной тариф с 22:00 до 08:00", g.Balance, 0);
            if (g.Balance < t.Price) return (false, "Недостаточно средств", g.Balance, 0);
            if (_active.ContainsKey(pc.Id) || _active.Values.Any(a => a.GuestId == g.Id))
                return (false, "Сеанс уже идёт на этом или другом ПК", g.Balance, 0);

            var now = Db.Now();
            var ends = now + t.Minutes * 60L;
            if (t.NightOnly) ends = Math.Min(ends, NightEnd(now)); // ночной пакет действует только до 08:00
            var minutes = (int)((ends - now) / 60);
            Db.Exec(c, "UPDATE guests SET balance = balance - $p WHERE id = $g", ("$p", t.Price), ("$g", g.Id));
            Db.Exec(c, "INSERT INTO sessions(guest_id, pc_id, tariff_id, started, ends, price) VALUES($g, $pc, $t, $s, $e, $pr)",
                ("$g", g.Id), ("$pc", pc.Id), ("$t", t.Id), ("$s", now), ("$e", ends), ("$pr", t.Price));
            var sid = Db.LastId(c);
            Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, session_id, ts) VALUES($g, $a, 'session', $n, $s, $t)",
                ("$g", g.Id), ("$a", -t.Price), ("$n", $"{t.Name} · {pc.Name}"), ("$s", sid), ("$t", now));
            tx.Commit();
            _active[pc.Id] = new SessionInfo(sid, g.Id, g.Login, pc.Id, pc.Name, t.Id, now, ends, null, t.Price, null);
            BindLogin(pc.Id, g.Id, ends + 30 * 60);
            return (true, null, g.Balance - t.Price, minutes);
        }
    }

    /// <summary>Завершает активную сессию ПК. Возвращает завершённую сессию или null, если активной не было.</summary>
    /// <summary>Ближайшие 08:00 по времени клуба (конец ночного тарифа), секунды Unix.</summary>
    private long NightEnd(long nowUnix)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(nowUnix), _tz);
        var end = new DateTime(local.Year, local.Month, local.Day, 8, 0, 0, DateTimeKind.Unspecified);
        if (local.Hour >= 8) end = end.AddDays(1);
        return new DateTimeOffset(end, _tz.GetUtcOffset(end)).ToUnixTimeSeconds();
    }

    /// <summary>expectedId: завершать, только если на ПК всё ещё именно этот сеанс (ExpiryWorker не должен закрыть новый).</summary>
    public SessionInfo? EndSession(long pcId, string reason, long? expectedId = null, bool onlyIfExpired = false)
    {
        lock (_writeLock)
        {
            if (!_active.TryGetValue(pcId, out var cur)) return null;
            if (expectedId is not null && cur.Id != expectedId) return null;
            if (onlyIfExpired && cur.Ends > Db.Now()) return null; // время успели добавить
            if (!_active.TryRemove(pcId, out var s)) return null;
            var now = Db.Now();
            using var c = _db.Open();
            Db.Exec(c, "UPDATE sessions SET ended = $t, reason = $r WHERE id = $i AND ended IS NULL", ("$t", now), ("$r", reason), ("$i", s.Id));
            return s with { Ended = now, Reason = reason };
        }
    }

    public SessionInfo? EndSessionById(long id, string reason)
    {
        var s = _active.Values.FirstOrDefault(x => x.Id == id);
        return s is null ? null : EndSession(s.PcId, reason);
    }

    // ---------- самостоятельная регистрация гостя ----------

    private static readonly System.Text.RegularExpressions.Regex LoginRx = new(@"^[A-Za-z0-9_.\-А-Яа-яЁё]{3,24}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly ConcurrentDictionary<long, Queue<long>> _regByPc = new();
    private readonly Queue<long> _regAll = new();

    /// <summary>Лимиты против массовых регистраций: не больше 10 в час с одного ПК и 120 в час по всему клубу.</summary>
    private bool RegistrationAllowed(long pcId)
    {
        var now = Db.Now();
        lock (_regAll)
        {
            while (_regAll.Count > 0 && now - _regAll.Peek() > 3600) _regAll.Dequeue();
            var q = _regByPc.GetOrAdd(pcId, _ => new Queue<long>());
            while (q.Count > 0 && now - q.Peek() > 3600) q.Dequeue();
            if (q.Count >= 10 || _regAll.Count >= 120) return false;
            q.Enqueue(now);
            _regAll.Enqueue(now);
            return true;
        }
    }

    /// <summary>Регистрация гостя с клубного ПК. Возвращает то же, что и вход, чтобы гость сразу попал к выбору тарифа.</summary>
    public (bool ok, string? error, Guest? guest, List<Tariff>? tariffs) Register(Pc pc, string login, string password, string? birth)
    {
        login = (login ?? "").Trim();
        if (!LoginRx.IsMatch(login)) return (false, "Логин: 3–24 символа, буквы, цифры, точка, дефис, подчёркивание", null, null);
        if (password is null || password.Length < 6 || password.Length > 64) return (false, "Пароль должен быть от 6 до 64 символов", null, null);
        if (password.Equals(login, StringComparison.OrdinalIgnoreCase)) return (false, "Пароль не должен совпадать с логином", null, null);
        if (!DateOnly.TryParseExact(birth ?? "", "yyyy-MM-dd", out var born)) return (false, "Укажите дату рождения", null, null);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz).DateTime);
        var age = today.Year - born.Year - (today < born.AddYears(today.Year - born.Year) ? 1 : 0);
        if (age < 6 || age > 100) return (false, "Проверьте дату рождения", null, null);
        if (!RegistrationAllowed(pc.Id)) return (false, "Слишком много регистраций, обратитесь к администратору", null, null);

        Result created;
        if (!_hashSlots.Wait(TimeSpan.FromSeconds(3))) return (false, "Сервер занят, попробуйте ещё раз", null, null);
        try { created = CreateGuest(login, password, 0, born.ToString("yyyy-MM-dd")); }
        finally { _hashSlots.Release(); }
        if (!created.Ok) return (false, created.Error!.Contains("уже есть") ? "Этот логин уже занят" : created.Error, null, null);

        using var c = _db.Open();
        var g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE login = $l", ReadGuest, ("$l", login));
        if (g is not null) { _guestPc[g.Id] = pc.Id; BindLogin(pc.Id, g.Id, Db.Now() + 30 * 60); }
        return (true, null, g, Tariffs(pc.Zone));
    }

    public SessionInfo? ActiveSessionOfPc(long pcId) => _active.GetValueOrDefault(pcId);

    /// <summary>Сеансы, у которых вышло время. Смотрит только в память: вызывается каждые 2 с.</summary>
    public List<SessionInfo> Expired()
    {
        var now = Db.Now();
        return _active.Values.Where(s => s.Ends <= now).ToList();
    }

    public List<SessionInfo> Sessions(bool activeOnly, int limit = 200)
    {
        if (activeOnly) return _active.Values.OrderByDescending(s => s.Id).Take(limit).ToList();
        using var c = _db.Open();
        return Db.Query(c, SessionSelect + " ORDER BY s.id DESC LIMIT $l", ReadSession, ("$l", limit));
    }

    public SessionInfo? AddTime(long sessionId, int minutes)
    {
        if (minutes is < -600 or > 600 || minutes == 0) return null;
        lock (_writeLock)
        {
            var s = _active.Values.FirstOrDefault(x => x.Id == sessionId);
            if (s is null) return null;
            var ends = s.Ends + minutes * 60L;
            if (ends <= Db.Now()) return null;
            using var c = _db.Open();
            Db.Exec(c, "UPDATE sessions SET ends = $e WHERE id = $i AND ended IS NULL", ("$e", ends), ("$i", sessionId));
            var updated = s with { Ends = ends };
            _active[s.PcId] = updated;
            return updated;
        }
    }

    private (long at, object value)? _statsCache;

    /// <summary>Сводка для панели. Считается не чаще раза в 3 секунды: панель может опрашивать часто.</summary>
        public object Stats()
    {
        if (_statsCache is { } sc && Db.Now() - sc.at < 3) return sc.value;
        using var c = _db.Open();
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz).Date, _tz.GetUtcOffset(DateTimeOffset.UtcNow)).ToUnixTimeSeconds();
        var value = new
        {
            pcs = Db.Scalar<long>(c, "SELECT COUNT(*) FROM pcs"),
            activeSessions = (long)_active.Count,
            guests = Db.Scalar<long>(c, "SELECT COUNT(*) FROM guests"),
            revenueToday = -(Db.Scalar<long?>(c, "SELECT SUM(amount) FROM tx WHERE kind = 'session' AND ts >= $s", ("$s", dayStart)) ?? 0),
            topupsToday = Db.Scalar<long?>(c, "SELECT SUM(amount) FROM tx WHERE kind = 'topup' AND ts >= $s", ("$s", dayStart)) ?? 0,
        };
        _statsCache = (Db.Now(), value);
        return value;
    }

    public void SeedDefaults(bool demo)
    {
        using var c = _db.Open();
        if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM tariffs") == 0)
        {
            UpsertTariff(new TariffReq("h1", "1 час", 60, 140, false, "ARENA", true, 1));
            UpsertTariff(new TariffReq("h3", "3 часа", 180, 350, false, "ARENA", true, 2));
            UpsertTariff(new TariffReq("h5", "5 часов", 300, 500, false, "ARENA", true, 3));
            UpsertTariff(new TariffReq("night", "Ночь", 600, 550, true, "ARENA", true, 4));
        }
        if (demo && Db.Scalar<long>(c, "SELECT COUNT(*) FROM guests") == 0)
        {
            CreateGuest("demo", "demo", 600, null);
            CreateGuest("vip", "vipvip", 2000, null);
        }
    }
}
