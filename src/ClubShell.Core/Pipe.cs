using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClubShell.Core;

/// <summary>
/// Канал «служба ↔ окно блокировки». Формат — по одному JSON-объекту на строку.
/// Токен ПК по каналу не передаётся: все запросы к серверу клуба делает служба (SYSTEM), окно получает только результат.
///
/// Окно → служба:
///   status    {locked, sessionActive}               — раз в 2 с; служба по нему проверяет, что окно живо и заблокировано
///   adminPin  {pin}                                 — запрос на выход для обслуживания
///   request   {id, op, username, password, birth, packageId} — op: login | register | logout | start | stop | catalog | speedtest
/// Служба → окно:
///   hello     {pcName, zone, requireService, serverUrl} — сразу после подключения (serverUrl — для обложек и пинга, не секрет)
///   state     {offline, reason}                     — раз в 2 с; offline=true значит «заблокировать»
///   adminResult {ok}                                — ответ на adminPin
///   response  {id, ok, error, fail, user, balance, packages, minutes, catalog, speed} — ответ на request (fail=true: нет связи с сервером)
///   command   {name, text, remainingSec, user, balance} — команда сервера: endSession | lock | message | sync | balance
/// </summary>
public static class PipeNames
{
    public const string Control = "ClubShell.Control";

    /// <summary>Мьютекс «одно окно блокировки»: создаёт служба (SYSTEM) в Global\, гость создать такое имя сам не может.</summary>
    public const string LockerMutex = @"Global\ClubShell.Locker";

    /// <summary>Строка от окна к службе: статус, PIN или запрос — больше не бывает.</summary>
    public const int MaxClientLine = 64 * 1024;

    /// <summary>Строка от службы к окну: каталог с сервера может быть большим.</summary>
    public const int MaxServerLine = 4 * 1024 * 1024;
}

public sealed class PipeMessage
{
    public string Type { get; set; } = "";

    public string? User { get; set; }
    public bool SessionActive { get; set; }
    public bool Locked { get; set; }
    public int RemainingSec { get; set; }
    public string? Pin { get; set; }

    // hello: адрес сервера (без токена: он есть только у службы)
    public string? ServerUrl { get; set; }

    // request / response
    public int Id { get; set; }
    public string? Op { get; set; }
    public string? Password { get; set; }
    public string? Birth { get; set; }
    public string? PackageId { get; set; }
    public string? Error { get; set; }
    public bool Fail { get; set; }
    public List<TariffPackage>? Packages { get; set; }
    public int Minutes { get; set; }
    public CatalogSnapshot? Catalog { get; set; }
    public SpeedResult? Speed { get; set; }

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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToLine() => JsonSerializer.Serialize(this, Options);

    public static PipeMessage? FromLine(string line)
    {
        try { return JsonSerializer.Deserialize<PipeMessage>(line, Options); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Чтение строк из канала с ограничением длины: StreamReader.ReadLineAsync копит строку без предела,
/// и клиент мог бы прислать гигабайт без перевода строки. Здесь превышение — InvalidDataException и обрыв канала.
/// </summary>
public sealed class PipeLineReader
{
    private readonly Stream _stream;
    private readonly int _maxBytes;
    private readonly byte[] _buf = new byte[8192];
    private readonly MemoryStream _line = new();
    private int _start, _end;

    public PipeLineReader(Stream stream, int maxBytes) => (_stream, _maxBytes) = (stream, maxBytes);

    /// <summary>Следующая строка без \r\n; null — канал закрыт.</summary>
    public async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        _line.SetLength(0);
        while (true)
        {
            var nl = Array.IndexOf(_buf, (byte)'\n', _start, _end - _start);
            if (nl >= 0)
            {
                Append(_start, nl - _start);
                _start = nl + 1;
                return Decode();
            }
            Append(_start, _end - _start);
            _start = _end = 0;

            var n = await _stream.ReadAsync(_buf, ct);
            if (n == 0) return _line.Length > 0 ? Decode() : null;
            _end = n;
        }
    }

    private void Append(int offset, int count)
    {
        if (count <= 0) return;
        if (_line.Length + count > _maxBytes) throw new InvalidDataException($"pipe line longer than {_maxBytes} bytes");
        _line.Write(_buf, offset, count);
    }

    private string Decode()
    {
        var len = (int)_line.Length;
        var bytes = _line.GetBuffer();
        if (len > 0 && bytes[len - 1] == '\r') len--;
        return Encoding.UTF8.GetString(bytes, 0, len);
    }
}
