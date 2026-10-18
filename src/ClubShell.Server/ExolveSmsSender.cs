using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace ClubShell.Server;

/// <summary>
/// Отправка SMS-кодов через МТС Exolve (POST https://api.exolve.ru/messaging/v1/SendSMS, заголовок Authorization: Bearer &lt;ключ&gt;).
/// Настройки: Exolve__ApiKey (ключ приложения из личного кабинета), Exolve__Sender (номер или альфа-имя отправителя).
/// В тестовый период Exolve сам подменяет текст SMS на «Это тестовое SMS от МТС Exolve…», код при этом не доходит:
/// чтобы коды работали, нужно подписать договор и зарегистрировать имя отправителя (альфа-имя доступно ИП и юрлицам).
/// </summary>
public sealed class ExolveSmsSender : ICodeDelivery
{
    private readonly HttpClient _http;
    private readonly string _sender;
    private readonly ILogger<ExolveSmsSender> _log;

    public ExolveSmsSender(IConfiguration cfg, ILogger<ExolveSmsSender> log)
    {
        _log = log;
        var key = cfg["Exolve:ApiKey"] ?? throw new InvalidOperationException("Exolve:ApiKey не задан");
        _sender = cfg["Exolve:Sender"] ?? throw new InvalidOperationException("Exolve:Sender (номер или альфа-имя отправителя) не задан");
        var apiBase = (cfg["Exolve:ApiBase"] ?? "https://api.exolve.ru").TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(apiBase + "/"), Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    public async Task<string?> DeliverAsync(string phone, string code)
    {
        var destination = phone.TrimStart('+'); // Exolve принимает номер цифрами: 79991112233
        using var resp = await _http.PostAsJsonAsync("messaging/v1/SendSMS", new
        {
            number = _sender,
            destination,
            text = $"Мираж PRO: код {code}. Никому не сообщайте его.",
        });
        if (!resp.IsSuccessStatusCode)
        {
            // тело ответа Exolve содержит причину (alpha name is not active, destination is not permitted …), в журнал, но не гостю
            _log.LogError("Exolve SendSMS: {Status} {Body}", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
            throw new HttpRequestException($"Exolve {(int)resp.StatusCode}");
        }
        return null;
    }
}
