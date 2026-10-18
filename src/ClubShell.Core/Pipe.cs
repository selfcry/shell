using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubShell.Core;

/// <summary>
/// Канал «служба ↔ окно блокировки». Формат — по одному JSON-объекту на строку.
///
/// Окно → служба:
///   status    {user, sessionActive, remainingSec}   — раз в 2 с, служба берёт отсюда имя гостя для пульса
///   adminPin  {pin}                                 — запрос на выход для обслуживания
/// Служба → окно:
///   hello     {pcName, zone, requireService}        — сразу после подключения
///   state     {offline, reason}                     — раз в 2 с; offline=true значит «заблокировать»
///   adminResult {ok}                                — ответ на adminPin
///   command   {name, text, remainingSec, user, balance} — команда сервера: endSession | lock | message | sync | balance
/// </summary>
public static class PipeNames
{
    public const string Control = "ClubShell.Control";
}

public sealed class PipeMessage
{
    public string Type { get; set; } = "";

    public string? User { get; set; }
    public bool SessionActive { get; set; }
    public int RemainingSec { get; set; }
    public string? Pin { get; set; }

    // hello: куда подключаться окну (токен ПК лежит только у службы и окна; гость вводит свои данные отдельно)
    public string? ServerUrl { get; set; }
    public string? PcToken { get; set; }

    // command: команда сервера окну (endSession, lock, message, sync)
    public string? Name { get; set; }
    public int Balance { get; set; }
    public string? Text { get; set; }

    public string? PcName { get; set; }
    public string? Zone { get; set; }
    public bool RequireService { get; set; } = true;
    public bool Offline { get; set; }
    public string? Reason { get; set; }
    public bool Ok { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string ToLine() => JsonSerializer.Serialize(this, Options);

    public static PipeMessage? FromLine(string line)
    {
        try { return JsonSerializer.Deserialize<PipeMessage>(line, Options); }
        catch (JsonException) { return null; }
    }
}
