using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClubShell.Server;

public sealed record Product(long Id, string Name, string? Category, int Price, int Stock, bool Active, int Sort);
public sealed record OrderLine(long ProductId, string Name, int Qty, int Price);
public sealed record ShopOrder(long Id, long? PcId, string? Pc, long? GuestId, string? Guest, List<OrderLine> Lines, int Total, string Method, string Status, long Ts);

/// <summary>Бар и склад: товары с остатками, продажа на стойке (наличные, карта, СБП, с баланса гостя) и заказ с ПК.</summary>
public sealed partial class ClubService
{
    private static readonly JsonSerializerOptions LinesJson = new(JsonSerializerDefaults.Web);

    private static Product ReadProduct(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5) == 1, r.GetInt32(6));
    private const string ProductCols = "id, name, category, price, stock, active, sort";

    public List<Product> Products(bool onlyActive)
    {
        using var c = _db.Open();
        return Db.Query(c, $"SELECT {ProductCols} FROM products {(onlyActive ? "WHERE active = 1" : "")} ORDER BY sort, category, name", ReadProduct);
    }

    public Result UpsertProduct(long? id, string name, string? category, int price, int? stock, bool active, int sort)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 60) return new Result(false, "Название до 60 символов");
        if (price is < 0 or > 100_000) return new Result(false, "Цена от 0 до 100 000 ₽");
        if (stock is < 0 or > 100_000) return new Result(false, "Остаток от 0 до 100 000");
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()[..Math.Min(category.Trim().Length, 30)];
        using var c = _db.Open();
        lock (_writeLock)
        {
            if (id is null)
                Db.Exec(c, "INSERT INTO products(name, category, price, stock, active, sort) VALUES($n, $c, $p, $s, $a, $o)",
                    ("$n", name), ("$c", category), ("$p", price), ("$s", stock ?? 0), ("$a", active ? 1 : 0), ("$o", sort));
            else if (Db.Exec(c, "UPDATE products SET name = $n, category = $c, price = $p, stock = COALESCE($s, stock), active = $a, sort = $o WHERE id = $i",
                         ("$n", name), ("$c", category), ("$p", price), ("$s", stock), ("$a", active ? 1 : 0), ("$o", sort), ("$i", id)) == 0)
                return new Result(false, "Товар не найден");
        }
        return new Result(true);
    }

    /// <summary>Приход товара на склад (или списание при отрицательном количестве).</summary>
    public Result AddStock(long productId, int qty)
    {
        if (qty == 0 || Math.Abs(qty) > 10_000) return new Result(false, "Количество от 1 до 10 000");
        using var c = _db.Open();
        lock (_writeLock)
            return Db.Exec(c, "UPDATE products SET stock = MAX(0, stock + $q) WHERE id = $i", ("$q", qty), ("$i", productId)) > 0 ? new Result(true) : new Result(false, "Товар не найден");
    }

    /// <summary>
    /// Продажа. method: cash | card | sbp — оплата на стойке; balance — с баланса гостя (guestLogin обязателен).
    /// fromPc — заказ гостя с ПК: проверяется, что гость вошёл на этом ПК; заказ ждёт выдачи в админке.
    /// </summary>
    public (Result res, ShopOrder? order) Sell(List<(long productId, int qty)> items, string method, string? guestLogin, Pc? fromPc)
    {
        if (items.Count is 0 or > 20 || items.Any(i => i.qty is < 1 or > 50)) return (new Result(false, "Пустой или слишком большой заказ"), null);
        if (method is not ("cash" or "card" or "sbp" or "balance")) return (new Result(false, "Неизвестный способ оплаты"), null);
        lock (_writeLock)
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            Guest? g = null;
            if (method == "balance" || guestLogin is { Length: > 0 })
            {
                g = Db.QueryOne(c, "SELECT id, login, balance, birth, banned, created FROM guests WHERE login = $l", ReadGuest, ("$l", (guestLogin ?? "").Trim()));
                if (g is null) return (new Result(false, "Гость не найден"), null);
                if (fromPc is not null && (!_loggedIn.TryGetValue(fromPc.Id, out var li) || li.guestId != g.Id || li.until < Db.Now())) return (new Result(false, "Войдите заново"), null);
            }
            var lines = new List<OrderLine>();
            foreach (var (productId, qty) in items.GroupBy(i => i.productId).Select(gr => (gr.Key, gr.Sum(x => x.qty))))
            {
                var p = Db.QueryOne(c, $"SELECT {ProductCols} FROM products WHERE id = $i AND active = 1", ReadProduct, ("$i", productId));
                if (p is null) return (new Result(false, "Товар не найден или снят с продажи"), null);
                if (p.Stock < qty) return (new Result(false, $"«{p.Name}»: осталось {p.Stock} шт."), null);
                lines.Add(new OrderLine(p.Id, p.Name, qty, p.Price));
            }
            var total = lines.Sum(l => l.Qty * l.Price);
            if (method == "balance" && g!.Balance < total) return (new Result(false, $"Недостаточно средств: нужно {total} ₽"), null);
            foreach (var l in lines) Db.Exec(c, "UPDATE products SET stock = stock - $q WHERE id = $i", ("$q", l.Qty), ("$i", l.ProductId));
            var now = Db.Now();
            var shift = OpenShiftId(c);
            Db.Exec(c, "INSERT INTO orders(pc_id, guest_id, lines, total, method, status, ts, shift_id) VALUES($pc, $g, $l, $t, $m, $s, $ts, $sh)",
                ("$pc", fromPc?.Id), ("$g", g?.Id), ("$l", JsonSerializer.Serialize(lines, LinesJson)), ("$t", total), ("$m", method),
                ("$s", fromPc is null ? "done" : "new"), ("$ts", now), ("$sh", shift));
            var orderId = Db.LastId(c);
            if (method == "balance")
            {
                Db.Exec(c, "UPDATE guests SET balance = balance - $t WHERE id = $g", ("$t", total), ("$g", g!.Id));
                Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts, shift_id) VALUES($g, $a, 'shop', $n, $ts, $sh)",
                    ("$g", g.Id), ("$a", -total), ("$n", $"Бар, заказ #{orderId}"), ("$ts", now), ("$sh", shift));
            }
            tx.Commit();
            return (new Result(true), OrderById(c, orderId));
        }
    }

    private static ShopOrder ReadOrder(SqliteDataReader r) => new(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetString(4),
        JsonSerializer.Deserialize<List<OrderLine>>(r.GetString(5), LinesJson) ?? new(), r.GetInt32(6), r.GetString(7), r.GetString(8), r.GetInt64(9));

    private const string OrderSelect = """
        SELECT o.id, o.pc_id, p.name, o.guest_id, g.login, o.lines, o.total, o.method, o.status, o.ts
        FROM orders o LEFT JOIN pcs p ON p.id = o.pc_id LEFT JOIN guests g ON g.id = o.guest_id
        """;

    private static ShopOrder? OrderById(SqliteConnection c, long id) => Db.QueryOne(c, OrderSelect + " WHERE o.id = $i", ReadOrder, ("$i", id));

    public List<ShopOrder> Orders(bool pendingOnly)
    {
        using var c = _db.Open();
        return pendingOnly
            ? Db.Query(c, OrderSelect + " WHERE o.status = 'new' ORDER BY o.id", ReadOrder)
            : Db.Query(c, OrderSelect + " ORDER BY o.id DESC LIMIT 200", ReadOrder);
    }

    /// <summary>Заказ с ПК выдан (done) или отменён (cancelled: товар вернётся на склад, деньги — на баланс).</summary>
    public Result FinishOrder(long id, bool cancel)
    {
        lock (_writeLock)
        {
            using var c = _db.Open();
            var o = OrderById(c, id);
            if (o is null || o.Status != "new") return new Result(false, "Заказ не найден или уже закрыт");
            using var tx = c.BeginTransaction();
            Db.Exec(c, "UPDATE orders SET status = $s WHERE id = $i", ("$s", cancel ? "cancelled" : "done"), ("$i", id));
            if (cancel)
            {
                foreach (var l in o.Lines) Db.Exec(c, "UPDATE products SET stock = stock + $q WHERE id = $i", ("$q", l.Qty), ("$i", l.ProductId));
                if (o.Method == "balance" && o.GuestId is { } gid)
                {
                    Db.Exec(c, "UPDATE guests SET balance = balance + $t WHERE id = $g", ("$t", o.Total), ("$g", gid));
                    Db.Exec(c, "INSERT INTO tx(guest_id, amount, kind, note, ts, shift_id) VALUES($g, $a, 'shop', $n, $ts, $sh)",
                        ("$g", gid), ("$a", o.Total), ("$n", $"Отмена заказа #{id}"), ("$ts", Db.Now()), ("$sh", OpenShiftId(c)));
                }
            }
            tx.Commit();
            return new Result(true);
        }
    }

    public int? BalanceOfLogin(string? login)
    {
        using var c = _db.Open();
        return Db.Scalar<int?>(c, "SELECT balance FROM guests WHERE login = $l", ("$l", (login ?? "").Trim()));
    }

    /// <summary>Продажи бара за смену по способам оплаты (для отчёта смены).</summary>
    public (int cash, int card, int sbp, int balance) ShopTotals(long shiftId)
    {
        using var c = _db.Open();
        int S(string m) => (int)(Db.Scalar<long?>(c, "SELECT SUM(total) FROM orders WHERE shift_id = $s AND method = $m AND status != 'cancelled'", ("$s", shiftId), ("$m", m)) ?? 0);
        return (S("cash"), S("card"), S("sbp"), S("balance"));
    }
}
