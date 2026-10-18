using System.Collections.Concurrent;

namespace ClubShell.Server;

/// <summary>
/// Как гость получает код подтверждения телефона. Возвращает подсказку для экрана ввода кода (или null).
/// Реализации: StubSmsSender (заглушка), TelegramCodeDelivery (бесплатно, через бота), в будущем — настоящий SMS-провайдер.
/// </summary>
public interface ICodeDelivery
{
    Task<string?> DeliverAsync(string phone, string code);
}

/// <summary>
/// Заглушка: ничего не отправляет, пишет сообщение в журнал сервера и хранит последние 50 в памяти
/// (админ видит их в API /api/admin/sms-outbox).
/// </summary>
public sealed class StubSmsSender : ICodeDelivery
{
    private readonly ILogger<StubSmsSender> _log;
    private readonly ConcurrentQueue<(long ts, string phone, string text)> _outbox = new();

    public StubSmsSender(ILogger<StubSmsSender> log) => _log = log;

    public Task<string?> DeliverAsync(string phone, string code)
    {
        var text = $"Мираж PRO: код подтверждения {code}. Никому не сообщайте его.";
        _log.LogWarning("SMS (заглушка) на {Phone}: {Text}", phone, text);
        _outbox.Enqueue((Db.Now(), phone, text));
        while (_outbox.Count > 50) _outbox.TryDequeue(out _);
        return Task.FromResult<string?>(null);
    }

    public IEnumerable<object> Outbox() => _outbox.Reverse().Select(m => new { ts = m.ts, phone = m.phone, text = m.text });
}
