using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClubShell.Server;

/// <summary>
/// Бесплатное подтверждение телефона через Telegram-бота (вместо платных SMS).
/// Гость на клубном ПК вводит номер → открывает бота → нажимает «Поделиться номером» → бот присылает код.
/// Номер в Telegram подтверждён самим Telegram: бот принимает только собственный контакт отправителя
/// (contact.user_id == from.id), поэтому чужой номер подтвердить нельзя.
/// Токен бота: переменная Telegram__BotToken (создаётся бесплатно у @BotFather), имя бота: Telegram__BotName.
/// </summary>
public sealed class TelegramCodeDelivery : BackgroundService, ICodeDelivery
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _botName;
    private readonly ILogger<TelegramCodeDelivery> _log;
    private readonly ConcurrentDictionary<string, (string code, long expires)> _codes = new(); // телефон -> код

    public TelegramCodeDelivery(IConfiguration cfg, ILogger<TelegramCodeDelivery> log)
    {
        _log = log;
        var token = cfg["Telegram:BotToken"] ?? throw new InvalidOperationException("Telegram:BotToken не задан");
        _botName = (cfg["Telegram:BotName"] ?? "").TrimStart('@');
        var apiBase = (cfg["Telegram:ApiBase"] ?? "https://api.telegram.org").TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri($"{apiBase}/bot{token}/"), Timeout = TimeSpan.FromSeconds(40) };
    }

    public Task<string?> DeliverAsync(string phone, string code)
    {
        _codes[phone] = (code, Db.Now() + 600);
        var bot = _botName.Length > 0 ? $"@{_botName}" : "бота клуба";
        return Task.FromResult<string?>($"Откройте в Telegram {bot}, нажмите «Старт» и «Поделиться номером». Бот пришлёт код.");
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        long offset = 0;
        _log.LogInformation("Telegram-бот для подтверждения телефонов запущен ({Bot})", _botName.Length > 0 ? "@" + _botName : "без имени");
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var resp = await _http.PostAsJsonAsync("getUpdates", new { offset, timeout = 25, allowed_updates = new[] { "message" } }, Json, stop);
                var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(Json, stop);
                if (!doc.TryGetProperty("result", out var result)) { await Task.Delay(5000, stop); continue; }
                foreach (var u in result.EnumerateArray())
                {
                    offset = u.GetProperty("update_id").GetInt64() + 1;
                    if (u.TryGetProperty("message", out var m)) await HandleAsync(m, stop);
                }
                // старые коды чистим здесь же, отдельный таймер не нужен
                foreach (var kv in _codes.Where(k => k.Value.expires < Db.Now()).ToList()) _codes.TryRemove(kv.Key, out _);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("Telegram: {Message}", ex.Message);
                try { await Task.Delay(5000, stop); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task HandleAsync(JsonElement m, CancellationToken ct)
    {
        var chat = m.GetProperty("chat").GetProperty("id").GetInt64();
        var fromId = m.TryGetProperty("from", out var f) && f.TryGetProperty("id", out var fid) ? fid.GetInt64() : 0;

        if (m.TryGetProperty("contact", out var contact))
        {
            var contactUser = contact.TryGetProperty("user_id", out var cu) ? cu.GetInt64() : -1;
            if (contactUser != fromId) { await SendAsync(chat, "Нужен именно ваш номер: нажмите кнопку «Поделиться номером» внизу.", ct); return; }
            var phone = ClubService.NormalizePhone(contact.GetProperty("phone_number").GetString());
            if (phone is not null && _codes.TryGetValue(phone, out var c) && c.expires >= Db.Now())
                await SendAsync(chat, $"Ваш код подтверждения: {c.code}\nВведите его на клубном компьютере.", ct);
            else
                await SendAsync(chat, "Для этого номера нет активной регистрации. Сначала начните регистрацию на компьютере клуба и введите тот же номер.", ct);
            return;
        }

        // любое другое сообщение (в т.ч. /start): предлагаем поделиться номером
        await SendAsync(chat, "Привет! Чтобы получить код для регистрации в клубе, нажмите «Поделиться номером».", ct, new
        {
            keyboard = new[] { new[] { new { text = "Поделиться номером", request_contact = true } } },
            resize_keyboard = true, one_time_keyboard = true,
        });
    }

    private async Task SendAsync(long chat, string text, CancellationToken ct, object? keyboard = null)
    {
        using var resp = await _http.PostAsJsonAsync("sendMessage", new { chat_id = chat, text, reply_markup = keyboard }, Json, ct);
        if (!resp.IsSuccessStatusCode) _log.LogWarning("Telegram sendMessage: {Status}", (int)resp.StatusCode);
    }
}
