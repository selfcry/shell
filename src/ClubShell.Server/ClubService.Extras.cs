using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

public sealed record ShiftInfo(long Id, long Opened, long? Closed, string Cashier, int CashOpen, int? CashClose, string? Note);
public sealed record Level(long Id, string Name, int MinSpent, int CashbackPct);
public sealed record Promo(string Code, int Bonus, int MaxUses, int Used, long? Expires, bool Active, long Created);
public sealed record Booking(long Id, long PcId, string Pc, long GuestId, string Guest, long Starts, long Ends, string Status, string? Note);
public sealed record AdminCall(long PcId, string Pc, string? Guest, long Ts);

/// <summary>Смены и касса, бронирование, бонусы и уровни, промокоды, вызов администратора.</summary>
public sealed partial class ClubService
{
    private DateTimeOffset Local(long unix) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(unix), _tz);

    // ======================= смены и касса =======================

    /// <summary>Открытая смена (не больше одной) или null.</summary>
    public ShiftInfo? CurrentShift()
    {
        using var c = _db.Open();
        return Db.QueryOne(c, "SELECT id, opened, closed, cashier, cash_open, cash_close, note FROM shifts WHERE closed IS NULL ORDER BY id DESC LIMIT 1", ReadShift);
    }

    private static ShiftInfo ReadShift(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.IsDBNull(2) ? null : r.GetInt64(2), r.GetString(3),
        r.GetInt32(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6));

    private static long? OpenShiftId(SqliteConnection c) => Db.Scalar<long?>(c, "SELECT id FROM shifts WHERE closed IS NULL ORDER BY id DESC LIMIT 1");

    public (Result res, ShiftInfo? shift) OpenShift(string cashier, int cashOpen)
    {
        cashier = (cashier ?? "").Trim();
        if (cashier.Length is 0 or > 40) return (new Result(false, "Укажите имя кассира"), null);
        if (cashOpen is < 0 or > 10_000_000) return (new Result(false, "Недопустимая сумма в кассе"), null);
        lock (_writeLock)
        {
            using var c = _db.Open();
            if (OpenShiftId(c) is not null) return (new Result(false, "Смена уже открыта"), null);
            Db.Exec(c, "INSERT INTO shifts(opened, cashier, cash_open) VALUES($t, $c, $m)", ("$t", Db.Now()), ("$c", cashier), ("$m", cashOpen));
            return (new Result(true), Db.QueryOne(c, "SELECT id, opened, closed, cashier, cash_open, cash_close, note FROM shifts WHERE id = $i", ReadShift, ("$i", Db.LastId(c))));
        }
    }

    public (Result res, ShiftReport? report) CloseShift(int cashCounted, string? note)
    {
        if (cashCounted is < 0 or > 10_000_000) return (new Result(false, "Недопустимая сумма в кассе"), null);
        long? id;
        lock (_writeLock)
        {
            using var c = _db.Open();
            id = OpenShiftId(c);
            if (id is null) return (new Result(false, "Смена не открыта"), null);
            Db.Exec(c, "UPDATE shifts SET closed = $t, cash_close = $m, note = $n WHERE id = $i",
                ("$t", Db.Now()), ("$m", cashCounted), ("$n", string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 300)]), ("$i", id));
        }
        return (new Result(true), ShiftReportOf(id.Value));
    }

    public sealed record ShiftReport(ShiftInfo Shift, int TopupCash, int TopupCard, int TopupSbp, int TopupOther, int Charges, int ChargesCash,
        int SessionRevenue, int Sessions, int BonusSpent, int NewGuests, int CashExpected, int? CashDiff);

    public ShiftReport? ShiftReportOf(long shiftId)
    {
        using var c = _db.Open();
        var sh = Db.QueryOne(c, "SELECT id, opened, closed, cashier, cash_open, cash_close, note FROM shifts WHERE id = $i", ReadShift, ("$i", shiftId));
        if (sh is null) return null;
        var to = sh.Closed ?? Db.Now();
        int Sum(string where, params (string, object?)[] a) => (int)(Db.Scalar<long?>(c, $"SELECT SUM(amount) FROM tx WHERE {where}", a) ?? 0);
        var p = ("$s", (object?)shiftId);
        var topCash = Sum("shift_id = $s AND kind = 'topup' AND method = 'cash'", p);
        var topCard = Sum("shift_id = $s AND kind = 'topup' AND method = 'card'", p);
        var topSbp = Sum("shift_id = $s AND kind = 'topup' AND method = 'sbp'", p);
        var topOther = Sum("shift_id = $s AND kind = 'topup' AND method IS NULL", p);
        var charges = -Sum("shift_id = $s AND kind = 'charge'", p);
        var chargesCash = -Sum("shift_id = $s AND kind = 'charge' AND method = 'cash'", p); // возврат наличными
        var revenue = -Sum("shift_id = $s AND kind = 'session'", p);
        var sessions = (int)Db.Scalar<long>(c, "SELECT COUNT(*) FROM sessions WHERE started >= $a AND started <= $b", ("$a", sh.Opened), ("$b", to));
        var bonusSpent = -(int)(Db.Scalar<long?>(c, "SELECT SUM(amount) FROM tx WHERE kind = 'bonus_spent' AND ts >= $a AND ts <= $b", ("$a", sh.Opened), ("$b", to)) ?? 0);
        var newGuests = (int)Db.Scalar<long>(c, "SELECT COUNT(*) FROM guests WHERE created >= $a AND created <= $b", ("$a", sh.Opened), ("$b", to));
        var expected = sh.CashOpen + topCash - chargesCash;
        return new ShiftReport(sh, topCash, topCard, topSbp, topOther, charges, chargesCash, revenue, sessions, bonusSpent, newGuests, expected,
            sh.CashClose is { } counted ? counted - expected : null);
    }

    public List<ShiftInfo> Shifts(int limit = 60)
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, opened, closed, cashier, cash_open, cash_close, note FROM shifts ORDER BY id DESC LIMIT $l", ReadShift, ("$l", limit));
    }

    public string ShiftReportText(ShiftReport r)
    {
        string T(long t) => Local(t).ToString("dd.MM HH:mm");
        var diff = r.CashDiff is null ? "" : r.CashDiff == 0 ? "\nКасса сходится ✅" : $"\nРасхождение кассы: {r.CashDiff:+#;-#} ₽ ⚠️";
        return $"""
            🧾 Смена #{r.Shift.Id} закрыта · {r.Shift.Cashier}
            {T(r.Shift.Opened)} — {(r.Shift.Closed is { } cl ? T(cl) : "сейчас")}
            Пополнения: наличные {r.TopupCash} ₽, карта {r.TopupCard} ₽, СБП {r.TopupSbp} ₽{(r.TopupOther > 0 ? $", прочее {r.TopupOther} ₽" : "")}
            Списания: {r.Charges} ₽
            Выручка за время: {r.SessionRevenue} ₽ ({r.Sessions} сеансов), бонусами оплачено {r.BonusSpent} ₽
            Новых гостей: {r.NewGuests}
            Наличные: ожидалось {r.CashExpected} ₽, посчитано {(r.Shift.CashClose?.ToString() ?? "—")} ₽{diff}
            """;
    }

    // ======================= аналитика =======================

    /// <summary>Сводка за период: выручка по дням, загрузка по часам, популярные тарифы и ПК.</summary>
    public object Analytics(int days)
    {
        days = Math.Clamp(days, 1, 90);
        using var c = _db.Open();
        var localNow = Local(Db.Now());
        var fromLocal = new DateTimeOffset(localNow.Date.AddDays(-(days - 1)), localNow.Offset);
        var from = fromLocal.ToUnixTimeSeconds();
        var offset = (long)localNow.Offset.TotalSeconds;

        var byDay = Db.Query(c, """
            SELECT (ts + $o) / 86400 AS d, -SUM(CASE WHEN kind = 'session' THEN amount ELSE 0 END), SUM(CASE WHEN kind = 'topup' THEN amount ELSE 0 END)
            FROM tx WHERE ts >= $f GROUP BY d ORDER BY d
            """, r => new { day = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(0) * 86400).ToString("yyyy-MM-dd"), revenue = r.GetInt64(1), topups = r.GetInt64(2) },
            ("$o", offset), ("$f", from));
        var byHour = Db.Query(c, """
            SELECT ((started + $o) % 86400) / 3600 AS h, COUNT(*) FROM sessions WHERE started >= $f GROUP BY h ORDER BY h
            """, r => new { hour = r.GetInt32(0), sessions = r.GetInt64(1) }, ("$o", offset), ("$f", from));
        var tariffs = Db.Query(c, """
            SELECT s.tariff_id, COALESCE(t.name, s.tariff_id), COUNT(*), SUM(s.price) FROM sessions s LEFT JOIN tariffs t ON t.id = s.tariff_id
            WHERE s.started >= $f GROUP BY s.tariff_id ORDER BY COUNT(*) DESC
            """, r => new { id = r.GetString(0), name = r.GetString(1), sessions = r.GetInt64(2), sum = r.GetInt64(3) }, ("$f", from));
        var pcs = Db.Query(c, """
            SELECT p.name, COUNT(s.id), COALESCE(SUM(MIN(COALESCE(s.ended, s.ends), $n) - s.started), 0) FROM pcs p
            LEFT JOIN sessions s ON s.pc_id = p.id AND s.started >= $f WHERE p.deleted = 0 GROUP BY p.id ORDER BY p.name
            """, r => new { pc = r.GetString(0), sessions = r.GetInt64(1), hours = Math.Round(r.GetInt64(2) / 3600.0, 1) }, ("$f", from), ("$n", Db.Now()));
        var top = Db.Query(c, """
            SELECT g.login, -SUM(x.amount) AS spent FROM tx x JOIN guests g ON g.id = x.guest_id
            WHERE x.kind = 'session' AND x.ts >= $f GROUP BY x.guest_id ORDER BY spent DESC LIMIT 10
            """, r => new { login = r.GetString(0), spent = r.GetInt64(1) }, ("$f", from));
        var totalSessions = Db.Scalar<long>(c, "SELECT COUNT(*) FROM sessions WHERE started >= $f", ("$f", from));
        var revenue = -(Db.Scalar<long?>(c, "SELECT SUM(amount) FROM tx WHERE kind = 'session' AND ts >= $f", ("$f", from)) ?? 0);
        return new
        {
            days, revenue, sessions = totalSessions, avgCheck = totalSessions == 0 ? 0 : revenue / totalSessions,
            byDay, byHour, tariffs, pcs, top,
        };
    }

    // ======================= бонусы и уровни =======================

    public int BonusOf(long guestId)
    {
        using var c = _db.Open();
        return Db.Scalar<int>(c, "SELECT bonus FROM guests WHERE id = $i", ("$i", guestId));
    }

    public List<Level> Levels()
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT id, name, min_spent, cashback_pct FROM levels ORDER BY min_spent", r => new Level(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
    }

    /// <summary>Заменить всю лестницу уровней (из админки).</summary>
    public Result SetLevels(List<(string name, int minSpent, int pct)> levels)
    {
        if (levels.Count > 10) return new Result(false, "Не больше 10 уровней");
        foreach (var l in levels)
            if (string.IsNullOrWhiteSpace(l.name) || l.name.Length > 24 || l.minSpent is < 0 or > 10_000_000 || l.pct is < 0 or > 50)
                return new Result(false, "Уровень: название до 24 символов, порог от 0 ₽, кешбэк 0–50%");
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            Db.Exec(c, "DELETE FROM levels");
            foreach (var l in levels.OrderBy(x => x.minSpent))
                Db.Exec(c, "INSERT INTO levels(name, min_spent, cashback_pct) VALUES($n, $m, $p)", ("$n", l.name.Trim()), ("$m", l.minSpent), ("$p", l.pct));
            tx.Commit();
        }
        return new Result(true);
    }

    /// <summary>Сколько гость потратил на сеансы (деньгами) за всё время — от этого зависит уровень.</summary>
    private static int SpentOf(SqliteConnection c, long guestId) =>
        (int)-(Db.Scalar<long?>(c, "SELECT SUM(amount) FROM tx WHERE guest_id = $g AND kind = 'session'", ("$g", guestId)) ?? 0);

    private static Level? LevelOf(SqliteConnection c, int spent) =>
        Db.QueryOne(c, "SELECT id, name, min_spent, cashback_pct FROM levels WHERE min_spent <= $s ORDER BY min_spent DESC LIMIT 1",
            r => new Level(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)), ("$s", spent));

    private static int CashbackPct(SqliteConnection c, long guestId) => LevelOf(c, SpentOf(c, guestId))?.CashbackPct ?? 0;

    /// <summary>Уровень гостя и сколько осталось до следующего (для оболочки и карточки гостя).</summary>
    public object Loyalty(long guestId)
    {
        using var c = _db.Open();
        var spent = SpentOf(c, guestId);
        var level = LevelOf(c, spent);
        var next = Db.QueryOne(c, "SELECT name, min_spent FROM levels WHERE min_spent > $s ORDER BY min_spent LIMIT 1", r => (name: r.GetString(0), min: r.GetInt32(1)), ("$s", spent));
        return new
        {
            bonus = Db.Scalar<int>(c, "SELECT bonus FROM guests WHERE id = $i", ("$i", guestId)),
            spent, level = level?.Name, cashbackPct = level?.CashbackPct ?? 0,
            nextLevel = next.name, toNext = next.name is null ? 0 : next.min - spent,
        };
    }

    /// <summary>Начислить или списать бонусы вручную (админ).</summary>
    public (Result res, int bonus) AdjustBonus(long guestId, int amount, string? note)
    {
        if (amount == 0 || Math.Abs(amount) > 100_000) return (new Result(false, "Недопустимая сумма"), 0);
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            var b = Db.Scalar<int?>(c, "SELECT bonus FROM guests WHERE id = $i", ("$i", guestId));
            if (b is null) return (new Result(false, "Гость не найден"), 0);
            if (b + amount < 0) return (new Result(false, "Бонусов меньше, чем списание"), b.Value);
            Db.Exec(c, "UPDATE guests SET bonus = bonus + $a WHERE id = $i", ("$a", amount), ("$i", guestId));
            Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts) VALUES($g, $a, 'bonus_admin', $n, $t)", ("$g", guestId), ("$a", amount), ("$n", note), ("$t", Db.Now()));
            tx.Commit();
            return (new Result(true), b.Value + amount);
        }
    }

    // ======================= промокоды =======================

    private static readonly System.Text.RegularExpressions.Regex PromoRx = new(@"^[A-Za-z0-9_-]{3,24}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public List<Promo> Promos()
    {
        using var c = _db.Open();
        return Db.Query(c, "SELECT code, bonus, max_uses, used, expires, active, created FROM promos ORDER BY created DESC",
            r => new Promo(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetInt32(5) == 1, r.GetInt64(6)));
    }

    public Result UpsertPromo(string code, int bonus, int maxUses, long? expires, bool active)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!PromoRx.IsMatch(code)) return new Result(false, "Код: 3–24 символа, латиница, цифры, - и _");
        if (bonus is < 1 or > 100_000 || maxUses is < 0 or > 1_000_000) return new Result(false, "Бонус от 1 ₽, лимит использований от 0 (без ограничений)");
        lock (_writeLock)
        {
            using var c = _db.Open();
            Db.Exec(c, """
                INSERT INTO promos(code, bonus, max_uses, expires, active, created) VALUES($c, $b, $m, $e, $a, $t)
                ON CONFLICT(code) DO UPDATE SET bonus = $b, max_uses = $m, expires = $e, active = $a
                """, ("$c", code), ("$b", bonus), ("$m", maxUses), ("$e", expires), ("$a", active ? 1 : 0), ("$t", Db.Now()));
        }
        return new Result(true);
    }

    /// <summary>Гость вводит промокод на ПК: один раз на гостя, в пределах лимита и срока.</summary>
    public (Result res, int bonus) RedeemPromo(Pc pc, string login, string code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        if (!PromoRx.IsMatch(code)) return (new Result(false, "Промокод не найден"), 0);
        lock (_writeLock)
        {
            using var c = _db.Open();
            var g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE login = $l", ReadGuest, ("$l", (login ?? "").Trim()));
            if (g is null || !_loggedIn.TryGetValue(pc.Id, out var li) || li.guestId != g.Id || li.until < Db.Now()) return (new Result(false, "Войдите заново"), 0);
            var p = Db.QueryOne(c, "SELECT code, bonus, max_uses, used, expires, active, created FROM promos WHERE code = $c",
                r => new Promo(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetInt32(5) == 1, r.GetInt64(6)), ("$c", code));
            if (p is null || !p.Active) return (new Result(false, "Промокод не найден"), 0);
            if (p.Expires is { } e && e < Db.Now()) return (new Result(false, "Срок промокода истёк"), 0);
            if (p.MaxUses > 0 && p.Used >= p.MaxUses) return (new Result(false, "Промокод уже закончился"), 0);
            using var tx = c.BeginTransaction();
            try { Db.Exec(c, "INSERT INTO promo_uses(code, guest_id, ts) VALUES($c, $g, $t)", ("$c", code), ("$g", g.Id), ("$t", Db.Now())); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return (new Result(false, "Вы уже использовали этот промокод"), 0); }
            Db.Exec(c, "UPDATE promos SET used = used + 1 WHERE code = $c", ("$c", code));
            Db.Exec(c, "UPDATE guests SET bonus = bonus + $b WHERE id = $g", ("$b", p.Bonus), ("$g", g.Id));
            Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts) VALUES($g, $a, 'promo', $n, $t)", ("$g", g.Id), ("$a", p.Bonus), ("$n", "Промокод " + code), ("$t", Db.Now()));
            tx.Commit();
            return (new Result(true), Db.Scalar<int>(c, "SELECT bonus FROM guests WHERE id = $g", ("$g", g.Id)));
        }
    }

    // ======================= бронирование =======================

    private const string BookingSelect = """
        SELECT b.id, b.pc_id, p.name, b.guest_id, g.login, b.starts, b.ends, b.status, b.note
        FROM bookings b JOIN pcs p ON p.id = b.pc_id JOIN guests g ON g.id = b.guest_id
        """;
    private static Booking ReadBooking(SqliteDataReader r) => new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4),
        r.GetInt64(5), r.GetInt64(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8));

    /// <summary>Бронь держит ПК за 15 минут до начала; гость может опоздать — бронь снимается через 30 минут после начала.</summary>
    private const int HoldBefore = 15 * 60, LateGrace = 30 * 60;

    public List<Booking> Bookings(bool upcomingOnly)
    {
        using var c = _db.Open();
        return upcomingOnly
            ? Db.Query(c, BookingSelect + " WHERE b.status = 'active' AND b.ends > $n ORDER BY b.starts", ReadBooking, ("$n", Db.Now()))
            : Db.Query(c, BookingSelect + " ORDER BY b.starts DESC LIMIT 200", ReadBooking);
    }

    public (Result res, Booking? booking) CreateBooking(long pcId, string guestLogin, long starts, int minutes, string? note)
    {
        if (minutes is < 30 or > 24 * 60) return (new Result(false, "Длительность брони от 30 минут до суток"), null);
        if (starts < Db.Now() - 300 || starts > Db.Now() + 30L * 86400) return (new Result(false, "Время брони: от сейчас до 30 дней вперёд"), null);
        var ends = starts + minutes * 60L;
        lock (_writeLock)
        {
            using var c = _db.Open();
            var g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE login = $l", ReadGuest, ("$l", (guestLogin ?? "").Trim()));
            if (g is null) return (new Result(false, "Гость не найден: бронь делается на логин гостя"), null);
            if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM pcs WHERE id = $i AND deleted = 0", ("$i", pcId)) == 0) return (new Result(false, "ПК не найден"), null);
            var clash = Db.Scalar<long>(c, "SELECT COUNT(*) FROM bookings WHERE pc_id = $p AND status = 'active' AND starts < $e AND ends > $s", ("$p", pcId), ("$s", starts), ("$e", ends));
            if (clash > 0) return (new Result(false, "На это время ПК уже забронирован"), null);
            Db.Exec(c, "INSERT INTO bookings(pc_id, guest_id, starts, ends, note, created) VALUES($p, $g, $s, $e, $n, $t)",
                ("$p", pcId), ("$g", g.Id), ("$s", starts), ("$e", ends), ("$n", string.IsNullOrWhiteSpace(note) ? null : note.Trim()), ("$t", Db.Now()));
            return (new Result(true), Db.QueryOne(c, BookingSelect + " WHERE b.id = $i", ReadBooking, ("$i", Db.LastId(c))));
        }
    }

    public bool CancelBooking(long id)
    {
        using var c = _db.Open();
        lock (_writeLock) return Db.Exec(c, "UPDATE bookings SET status = 'cancelled' WHERE id = $i AND status = 'active'", ("$i", id)) > 0;
    }

    /// <summary>Чужая бронь, которая держит ПК прямо сейчас (с 15 минут до начала и до 30 минут опоздания).</summary>
    private (long starts, long ends)? ForeignBookingNow(long pcId, long guestId)
    {
        using var c = _db.Open();
        var now = Db.Now();
        return Db.QueryOne(c, "SELECT starts, ends FROM bookings WHERE pc_id = $p AND guest_id != $g AND status = 'active' AND starts - $hb <= $n AND starts + $lg > $n ORDER BY starts LIMIT 1",
            r => ((long, long)?)(r.GetInt64(0), r.GetInt64(1)), ("$p", pcId), ("$g", guestId), ("$n", now), ("$hb", HoldBefore), ("$lg", LateGrace));
    }

    /// <summary>Чужая бронь, которая начнётся раньше, чем закончится сеанс [from, to).</summary>
    private static (long starts, long ends)? NextForeignBooking(SqliteConnection c, long pcId, long guestId, long from, long to) =>
        Db.QueryOne(c, "SELECT starts, ends FROM bookings WHERE pc_id = $p AND guest_id != $g AND status = 'active' AND starts + $lg > $f AND starts - $hb < $t ORDER BY starts LIMIT 1",
            r => ((long, long)?)(r.GetInt64(0), r.GetInt64(1)), ("$p", pcId), ("$g", guestId), ("$f", from), ("$t", to), ("$hb", HoldBefore), ("$lg", LateGrace));

    /// <summary>Брони, на которые гость не пришёл: снимаются (вызывается фоновым обслуживанием).</summary>
    public int ExpireBookings()
    {
        using var c = _db.Open();
        lock (_writeLock) return Db.Exec(c, "UPDATE bookings SET status = 'missed' WHERE status = 'active' AND starts + $lg < $n", ("$lg", LateGrace), ("$n", Db.Now()));
    }

    /// <summary>Ближайшая активная бронь каждого ПК (для схемы зала).</summary>
    public Dictionary<long, Booking> NextBookings()
    {
        var result = new Dictionary<long, Booking>();
        foreach (var b in Bookings(upcomingOnly: true)) result.TryAdd(b.PcId, b);
        return result;
    }

    // ======================= вызов администратора =======================

    private readonly ConcurrentDictionary<long, AdminCall> _calls = new();

    /// <summary>Гость нажал «Позвать администратора». false — уже звал меньше минуты назад.</summary>
    public bool CallAdmin(Pc pc, string? login)
    {
        var now = Db.Now();
        if (_calls.TryGetValue(pc.Id, out var prev) && now - prev.Ts < 60) return false;
        var guest = _active.TryGetValue(pc.Id, out var s) ? s.Guest : login is { Length: > 0 and <= 32 } ? login : null;
        _calls[pc.Id] = new AdminCall(pc.Id, pc.Name, guest, now);
        return true;
    }

    public List<AdminCall> Calls() => _calls.Values.OrderBy(x => x.Ts).ToList();
    public bool AckCall(long pcId) => _calls.TryRemove(pcId, out _);

    // ======================= настройки (Telegram) =======================

    public string? Setting(string key)
    {
        using var c = _db.Open();
        return Db.Scalar<string>(c, "SELECT value FROM meta WHERE key = $k", ("$k", key));
    }

    public void SetSetting(string key, string? value)
    {
        using var c = _db.Open();
        lock (_writeLock)
        {
            if (string.IsNullOrEmpty(value)) Db.Exec(c, "DELETE FROM meta WHERE key = $k", ("$k", key));
            else Db.Exec(c, "INSERT INTO meta(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v", ("$k", key), ("$v", value));
        }
    }

    /// <summary>Уровни по умолчанию: при первом запуске (админ может поменять).</summary>
    private void SeedLevels()
    {
        using var c = _db.Open();
        if (Db.Scalar<long>(c, "SELECT COUNT(*) FROM levels") > 0) return;
        SetLevels(new() { ("Новичок", 0, 0), ("Бронза", 2000, 3), ("Серебро", 5000, 5), ("Золото", 10000, 7), ("Платина", 25000, 10) });
    }
}
