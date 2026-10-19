using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

public sealed record CabinetGuest(long Id, string Login, int Balance, int Bonus, string? Phone, string? Birth);
public sealed record Payment(string Id, long GuestId, int Amount, string Status, long Created, long? Paid);

/// <summary>
/// Личный кабинет гостя в браузере (cryvx.ru): регистрация, вход, баланс и история, брони, промокоды, онлайн-пополнение.
/// Вход выдаёт токен на 30 дней; на сервере хранится только его хеш.
/// </summary>
public sealed partial class ClubService
{
    private const int CabinetTokenTtl = 30 * 86400;
    private readonly ConcurrentDictionary<string, Queue<long>> _webRegByIp = new();
    private readonly ConcurrentDictionary<string, (int fails, long until)> _webFails = new(StringComparer.Ordinal);

    private static CabinetGuest ReadCabinet(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5));
    private const string CabinetCols = "id, login, balance, bonus, phone, birth";

    private string IssueGuestToken(SqliteConnection c, long guestId)
    {
        var token = "g_" + Security.NewToken();
        Db.Exec(c, "INSERT INTO guest_tokens(hash, guest_id, created, expires) VALUES($h, $g, $t, $e)",
            ("$h", Security.Sha256(token)), ("$g", guestId), ("$t", Db.Now()), ("$e", Db.Now() + CabinetTokenTtl));
        Db.Exec(c, "DELETE FROM guest_tokens WHERE expires < $n", ("$n", Db.Now()));
        return token;
    }

    /// <summary>Гость по токену кабинета (заблокированный гость не входит).</summary>
    public CabinetGuest? GuestByToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith("g_", StringComparison.Ordinal) || token.Length > 80) return null;
        using var c = _db.Open();
        return Db.QueryOne(c, $"SELECT g.{CabinetCols.Replace(", ", ", g.")} FROM guest_tokens t JOIN guests g ON g.id = t.guest_id WHERE t.hash = $h AND t.expires > $n AND g.banned = 0",
            ReadCabinet, ("$h", Security.Sha256(token)), ("$n", Db.Now()));
    }

    public void LogoutGuest(string token)
    {
        using var c = _db.Open();
        lock (_writeLock) Db.Exec(c, "DELETE FROM guest_tokens WHERE hash = $h", ("$h", Security.Sha256(token)));
    }

    /// <summary>Регистрация из кабинета: те же правила, что на ПК, не больше 5 регистраций в час с одного IP.</summary>
    public (string? token, string? error) RegisterWeb(string ip, string login, string password, string? birth, string? phone)
    {
        login = (login ?? "").Trim();
        if (ValidateRegistration(login, password, birth, out var born) is { } bad) return (null, bad);
        var now = Db.Now();
        var q = _webRegByIp.GetOrAdd(ip, _ => new Queue<long>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > 3600) q.Dequeue();
            if (q.Count >= 5) return (null, "Слишком много регистраций, попробуйте позже");
            q.Enqueue(now);
        }
        Result created;
        if (!_hashSlots.Wait(TimeSpan.FromSeconds(3))) return (null, "Сервер занят, попробуйте ещё раз");
        try { created = CreateGuest(login, password, 0, born.ToString("yyyy-MM-dd")); }
        finally { _hashSlots.Release(); }
        if (!created.Ok) return (null, created.Error!.Contains("уже есть") ? "Этот логин уже занят" : created.Error);
        using var c = _db.Open();
        var id = Db.Scalar<long>(c, "SELECT id FROM guests WHERE login = $l", ("$l", login));
        if (!string.IsNullOrWhiteSpace(phone)) SetPhone(id, phone);
        lock (_writeLock) return (IssueGuestToken(c, id), null);
    }

    public (string? token, string? error) LoginWeb(string ip, string login, string password)
    {
        var key = Norm(login);
        var now = Db.Now();
        foreach (var k in new[] { "w:" + key, "ip:" + ip })
            if (_webFails.TryGetValue(k, out var f) && f.until > now) return (null, $"Слишком много попыток, подождите {f.until - now} с");
        if (key.Length is 0 or > 32 || password is null || password.Length > 128) return (null, "Неверный логин или пароль");
        using var c = _db.Open();
        var row = Db.QueryOne(c, "SELECT id, banned, pass FROM guests WHERE login = $l", r => (id: r.GetInt64(0), banned: r.GetInt32(1) == 1, pass: r.GetString(2)), ("$l", login.Trim()));
        if (!_hashSlots.Wait(TimeSpan.FromSeconds(3))) return (null, "Сервер занят, попробуйте ещё раз");
        bool ok;
        try { ok = row.pass is not null && Security.VerifyPassword(password, row.pass); }
        finally { _hashSlots.Release(); }
        if (!ok)
        {
            foreach (var k in new[] { "w:" + key, "ip:" + ip })
            {
                var f = _webFails.GetValueOrDefault(k);
                _webFails[k] = f.fails + 1 >= (k.StartsWith("ip:") ? 20 : 5) ? (0, now + 300) : (f.fails + 1, 0);
            }
            return (null, "Неверный логин или пароль");
        }
        if (row.banned) return (null, "Учётная запись заблокирована");
        _webFails.TryRemove("w:" + key, out _);
        lock (_writeLock) return (IssueGuestToken(c, row.id), null);
    }

    public Result ChangePasswordWeb(long guestId, string oldPassword, string newPassword)
    {
        if (newPassword is null || newPassword.Length is < 6 or > 64) return new Result(false, "Новый пароль — от 6 до 64 символов");
        using var c = _db.Open();
        var pass = Db.Scalar<string>(c, "SELECT pass FROM guests WHERE id = $i", ("$i", guestId));
        if (pass is null || !Security.VerifyPassword(oldPassword ?? "", pass)) return new Result(false, "Текущий пароль неверный");
        lock (_writeLock)
        {
            Db.Exec(c, "UPDATE guests SET pass = $p WHERE id = $i", ("$p", Security.HashPassword(newPassword)), ("$i", guestId));
            Db.Exec(c, "DELETE FROM guest_tokens WHERE guest_id = $i", ("$i", guestId)); // выйти везде
        }
        return new Result(true);
    }

    /// <summary>Свободные ПК зоны на интервал (для брони из кабинета).</summary>
    public List<object> FreePcs(string? zone, long starts, int minutes)
    {
        var ends = starts + minutes * 60L;
        using var c = _db.Open();
        return Db.Query<object>(c, """
            SELECT p.id, p.name, p.zone FROM pcs p
            WHERE p.deleted = 0 AND ($z IS NULL OR p.zone = $z)
              AND NOT EXISTS (SELECT 1 FROM bookings b WHERE b.pc_id = p.id AND b.status = 'active' AND b.starts < $e AND b.ends > $s)
            ORDER BY p.name
            """, r => new { id = r.GetInt64(0), name = r.GetString(1), zone = r.GetString(2) }, ("$z", zone), ("$s", starts), ("$e", ends));
    }

    public List<string> Zones()
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT DISTINCT zone FROM pcs WHERE deleted = 0 ORDER BY zone", r => r.GetString(0));
    }

    /// <summary>Бронь из кабинета: не больше одной активной, не дальше 7 дней, ПК сейчас свободен на это время.</summary>
    public (Result res, Booking? booking) BookWeb(CabinetGuest g, long pcId, long starts, int minutes)
    {
        if (minutes is < 60 or > 12 * 60) return (new Result(false, "Бронь — от 1 до 12 часов"), null);
        if (starts < Db.Now() + 10 * 60) return (new Result(false, "Бронь — не раньше чем через 10 минут"), null);
        if (starts > Db.Now() + 7 * 86400) return (new Result(false, "Бронь — не дальше чем на неделю вперёд"), null);
        using (var c = _db.Open())
            if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM bookings WHERE guest_id = $g AND status = 'active' AND ends > $n", ("$g", g.Id), ("$n", Db.Now())) > 0)
                return (new Result(false, "У вас уже есть бронь: отмените её, чтобы сделать новую"), null);
        return CreateBooking(pcId, g.Login, starts, minutes, "из кабинета");
    }

    public List<Booking> GuestBookings(long guestId)
    {
        using var c = _db.Open();
        return Db.Query(c, BookingSelect + " WHERE b.guest_id = $g ORDER BY b.starts DESC LIMIT 20", ReadBooking, ("$g", guestId));
    }

    public bool CancelGuestBooking(long guestId, long bookingId)
    {
        using var c = _db.Open();
        lock (_writeLock) return Db.Exec(c, "UPDATE bookings SET status = 'cancelled' WHERE id = $i AND guest_id = $g AND status = 'active'", ("$i", bookingId), ("$g", guestId)) > 0;
    }

    /// <summary>Промокод из кабинета (без ПК).</summary>
    public (Result res, int bonus) RedeemPromoWeb(long guestId, string code)
    {
        // та же логика, что на ПК, но гость уже опознан токеном кабинета
        using (var c = _db.Open())
        {
            var login = Db.Scalar<string>(c, "SELECT login FROM guests WHERE id = $i", ("$i", guestId));
            if (login is null) return (new Result(false, "Гость не найден"), 0);
            _loggedIn[-guestId] = (guestId, Db.Now() + 60); // «виртуальный ПК» кабинета: RedeemPromo проверяет вход
            try { return RedeemPromo(new Pc(-guestId, "кабинет", "WEB", 0), login, code); }
            finally { _loggedIn.TryRemove(-guestId, out _); }
        }
    }

    // ======================= онлайн-пополнение (ЮKassa) =======================

    public Payment? PaymentById(string id)
    {
        using var c = _db.Open();
        return Db.QueryOne(c, "SELECT id, guest_id, amount, status, created, paid FROM payments WHERE id = $i",
            r => new Payment(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetInt64(4), r.IsDBNull(5) ? null : r.GetInt64(5)), ("$i", id));
    }

    public void SavePayment(string id, long guestId, int amount)
    {
        using var c = _db.Open();
        lock (_writeLock) Db.Exec(c, "INSERT INTO payments(id, guest_id, amount, status, created) VALUES($i, $g, $a, 'pending', $t)", ("$i", id), ("$g", guestId), ("$a", amount), ("$t", Db.Now()));
    }

    /// <summary>Платёж подтверждён ЮKassa: зачисляем ровно один раз (повторное уведомление ничего не меняет).</summary>
    public (bool credited, int balance) CompletePayment(string id, int paidAmount)
    {
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            var p = Db.QueryOne(c, "SELECT guest_id, amount, status FROM payments WHERE id = $i", r => (g: r.GetInt64(0), a: r.GetInt32(1), s: r.GetString(2)), ("$i", id));
            if (p.s != "pending" || p.a != paidAmount) return (false, 0);
            Db.Exec(c, "UPDATE payments SET status = 'succeeded', paid = $t WHERE id = $i", ("$t", Db.Now()), ("$i", id));
            Db.Exec(c, "UPDATE guests SET balance = balance + $a WHERE id = $g", ("$a", p.a), ("$g", p.g));
            Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts, method, shift_id) VALUES($g, $a, 'topup', $n, $t, 'online', $sh)",
                ("$g", p.g), ("$a", p.a), ("$n", "Онлайн-пополнение " + id), ("$t", Db.Now()), ("$sh", OpenShiftId(c)));
            tx.Commit();
            return (true, Db.Scalar<int>(c, "SELECT balance FROM guests WHERE id = $g", ("$g", p.g)));
        }
    }

    public void FailPayment(string id)
    {
        using var c = _db.Open();
        lock (_writeLock) Db.Exec(c, "UPDATE payments SET status = 'canceled' WHERE id = $i AND status = 'pending'", ("$i", id));
    }

    public List<Payment> GuestPayments(long guestId)
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, guest_id, amount, status, created, paid FROM payments WHERE guest_id = $g ORDER BY created DESC LIMIT 20",
            r => new Payment(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetInt64(4), r.IsDBNull(5) ? null : r.GetInt64(5)), ("$g", guestId));
    }
}
