using System.Net.Http.Json;

namespace ClubShell.Server;

/// <summary>
/// Уведомления в Telegram (вызов администратора, закрытие смены). Токен бота и чат задаются в админке
/// и хранятся на сервере (таблица meta), странице не возвращаются.
/// </summary>
public sealed class Telegram
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ClubService _club;
    private readonly ILogger<Telegram> _log;

    public Telegram(ClubService club, ILogger<Telegram> log) => (_club, _log) = (club, log);

    public bool Configured => !string.IsNullOrEmpty(_club.Setting("tg_token")) && !string.IsNullOrEmpty(_club.Setting("tg_chat"));

    /// <summary>Отправить и не ждать: уведомление не должно задерживать ответ кассиру или гостю.</summary>
    public void Notify(string text) => _ = SendAsync(text);

    public async Task<(bool ok, string? error)> SendAsync(string text)
    {
        var token = _club.Setting("tg_token");
        var chat = _club.Setting("tg_chat");
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chat)) return (false, "Telegram не настроен");
        try
        {
            using var r = await Http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text, disable_web_page_preview = true });
            if (r.IsSuccessStatusCode) return (true, null);
            var body = await r.Content.ReadAsStringAsync();
            _log.LogWarning("Telegram: {Status} {Body}", (int)r.StatusCode, body.Length > 200 ? body[..200] : body);
            return (false, r.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "Неверный токен бота" : "Telegram отклонил сообщение: проверьте чат и что бот добавлен в него");
        }
        catch (Exception ex)
        {
            _log.LogWarning("Telegram недоступен: {Error}", ex.Message);
            return (false, "Telegram недоступен");
        }
    }
}
