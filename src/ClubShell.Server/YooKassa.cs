using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ClubShell.Server;

/// <summary>
/// Онлайн-пополнение через ЮKassa. Включается, когда в админке («Настройки») заданы shopId и секретный ключ магазина.
/// Зачисление — только после того, как сервер сам спросил у ЮKassa статус платежа (уведомлениям и редиректам не верим).
/// </summary>
public sealed class YooKassa
{
    private static readonly HttpClient Http = new() { BaseAddress = new Uri("https://api.yookassa.ru/v3/"), Timeout = TimeSpan.FromSeconds(15) };
    private readonly ClubService _club;
    private readonly ILogger<YooKassa> _log;

    public YooKassa(ClubService club, ILogger<YooKassa> log) => (_club, _log) = (club, log);

    public bool Configured => !string.IsNullOrEmpty(_club.Setting("yk_shop")) && !string.IsNullOrEmpty(_club.Setting("yk_secret"));

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_club.Setting("yk_shop")}:{_club.Setting("yk_secret")}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        return req;
    }

    /// <summary>Создать платёж. Возвращает ссылку на оплату (страница ЮKassa) или ошибку.</summary>
    public async Task<(string? url, string? error)> CreateAsync(CabinetGuest g, int amount, string returnUrl)
    {
        if (!Configured) return (null, "Онлайн-пополнение пока не подключено: пополните баланс на стойке");
        if (amount is < 100 or > 15_000) return (null, "Сумма пополнения — от 100 до 15 000 ₽");
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new { value = $"{amount}.00", currency = "RUB" },
            ["capture"] = true,
            ["confirmation"] = new { type = "redirect", return_url = returnUrl },
            ["description"] = $"Пополнение баланса Мираж PRO: {g.Login}",
            ["metadata"] = new { guest_id = g.Id.ToString() },
        };
        // чек по 54-ФЗ: если в ЮKassa подключены «Чеки от ЮKassa», нужен контакт покупателя
        if (_club.Setting("yk_receipt") == "1" && g.Phone is { Length: > 0 } phone)
            body["receipt"] = new
            {
                customer = new { phone = phone.TrimStart('+') },
                items = new[] { new { description = "Пополнение игрового баланса", quantity = "1.00", amount = new { value = $"{amount}.00", currency = "RUB" }, vat_code = int.TryParse(_club.Setting("yk_vat"), out var v) ? v : 1, payment_subject = "service", payment_mode = "full_payment" } },
            };
        using var req = Request(HttpMethod.Post, "payments");
        req.Headers.Add("Idempotence-Key", Guid.NewGuid().ToString());
        req.Content = JsonContent.Create(body);
        try
        {
            using var resp = await Http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("ЮKassa create: {Status} {Body}", (int)resp.StatusCode, json.Length > 300 ? json[..300] : json);
                return (null, "Платёжная система отклонила запрос, попробуйте позже");
            }
            using var doc = JsonDocument.Parse(json);
            var id = doc.RootElement.GetProperty("id").GetString()!;
            var url = doc.RootElement.GetProperty("confirmation").GetProperty("confirmation_url").GetString();
            _club.SavePayment(id, g.Id, amount);
            return (url, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning("ЮKassa недоступна: {Error}", ex.Message);
            return (null, "Платёжная система недоступна, попробуйте позже");
        }
    }

    /// <summary>Спросить у ЮKassa статус и зачислить, если оплачено. Безопасно вызывать сколько угодно раз.</summary>
    public async Task<string> SyncAsync(string paymentId)
    {
        var local = _club.PaymentById(paymentId);
        if (local is null) return "unknown";
        if (local.Status != "pending" || !Configured) return local.Status;
        try
        {
            using var req = Request(HttpMethod.Get, "payments/" + Uri.EscapeDataString(paymentId));
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return local.Status;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var status = doc.RootElement.GetProperty("status").GetString();
            if (status == "succeeded")
            {
                var value = decimal.Parse(doc.RootElement.GetProperty("amount").GetProperty("value").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var (credited, _) = _club.CompletePayment(paymentId, (int)value);
                if (credited) _log.LogWarning("Онлайн-пополнение {Id}: +{Amount} ₽ гостю #{Guest}", paymentId, (int)value, local.GuestId);
                return "succeeded";
            }
            if (status == "canceled") { _club.FailPayment(paymentId); return "canceled"; }
            return "pending";
        }
        catch (Exception ex)
        {
            _log.LogWarning("ЮKassa статус {Id}: {Error}", paymentId, ex.Message);
            return local.Status;
        }
    }
}
