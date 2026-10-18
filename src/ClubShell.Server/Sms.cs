using System.Collections.Concurrent;

namespace ClubShell.Server;

public interface ISmsSender
{
    Task SendAsync(string phone, string text);
}

/// <summary>
/// Заглушка отправки SMS: ничего не отправляет, пишет сообщение в журнал сервера и хранит последние 50 в памяти
/// (админ видит их в API /api/admin/sms-outbox). Настоящего провайдера (SMS.ru, SMSC и т.п.) подключают заменой этого класса.
/// </summary>
public sealed class StubSmsSender : ISmsSender
{
    private readonly ILogger<StubSmsSender> _log;
    private readonly ConcurrentQueue<(long ts, string phone, string text)> _outbox = new();

    public StubSmsSender(ILogger<StubSmsSender> log) => _log = log;

    public Task SendAsync(string phone, string text)
    {
        _log.LogWarning("SMS (заглушка) на {Phone}: {Text}", phone, text);
        _outbox.Enqueue((Db.Now(), phone, text));
        while (_outbox.Count > 50) _outbox.TryDequeue(out _);
        return Task.CompletedTask;
    }

    public IEnumerable<object> Outbox() => _outbox.Reverse().Select(m => new { ts = m.ts, phone = m.phone, text = m.text });
}
