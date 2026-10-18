using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;

namespace ClubShell.Locker;

internal sealed record SpeedResult(bool Ok, double DownMbps, double UpMbps, long? PingMs, string? Error, string? Source);

/// <summary>
/// Пинг и тест скорости. Сначала меряем до сервера клуба (он всегда доступен ПК и не зависит от сторонних сайтов),
/// если сервер не настроен или недоступен — через speed.cloudflare.com. Тест не длиннее ~6 секунд на направление.
/// </summary>
internal static class NetworkTest
{
    private static string? _serverUrl;
    private static string? _token;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static int _running;

    /// <summary>Адрес сервера клуба и токен ПК: после этого тест и пинг идут к серверу.</summary>
    public static void Configure(string? serverUrl, string? token)
    {
        _serverUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/');
        _token = token;
    }

    /// <summary>Узел для пинга: сервер клуба, если он известен, иначе публичный DNS.</summary>
    public static string PingHost
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("CLUBSHELL_PING_HOST");
            if (!string.IsNullOrEmpty(env)) return env;
            return _serverUrl is not null && Uri.TryCreate(_serverUrl, UriKind.Absolute, out var u) && u.Host is not ("127.0.0.1" or "localhost") ? u.Host : "1.1.1.1";
        }
    }

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    public static async Task<long?> PingAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(PingHost, 1500);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception) { return null; }
    }

    public static async Task<SpeedResult> RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return new SpeedResult(false, 0, 0, null, "Тест уже идёт", null);
        try
        {
            var ping = await PingAsync();
            var errors = new List<string>();

            if (_serverUrl is not null && _token is not null)
            {
                try
                {
                    var down = await DownloadAsync($"{_serverUrl}/api/pc/speedtest/down?bytes=25000000", _token);
                    var up = await UploadAsync($"{_serverUrl}/api/pc/speedtest/up", _token, 4_000_000);
                    return new SpeedResult(true, down, up, ping, null, "до сервера клуба");
                }
                catch (Exception ex) { errors.Add("сервер клуба: " + Describe(ex)); }
            }

            try
            {
                var down = await DownloadAsync("https://speed.cloudflare.com/__down?bytes=50000000", null);
                var up = await UploadAsync("https://speed.cloudflare.com/__up", null, 5_000_000);
                return new SpeedResult(true, down, up, ping, null, "интернет (Cloudflare)");
            }
            catch (Exception ex) { errors.Add("интернет: " + Describe(ex)); }

            Debug.WriteLine("[ClubShell] speedtest failed: " + string.Join("; ", errors));
            return new SpeedResult(false, 0, 0, ping, "Не удалось измерить: " + string.Join("; ", errors), null);
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => "подождите 10 секунд и повторите",
        HttpRequestException { StatusCode: { } code } => $"ответ {(int)code}",
        HttpRequestException => "нет соединения",
        TaskCanceledException => "превышено время ожидания",
        _ => ex.Message,
    };

    private static HttpRequestMessage Req(HttpMethod method, string url, string? token)
    {
        var r = new HttpRequestMessage(method, url);
        if (token is not null) r.Headers.Add("X-Pc-Token", token);
        return r;
    }

    private static async Task<double> DownloadAsync(string url, string? token)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var sw = Stopwatch.StartNew();
        long total = 0;
        try
        {
            using var req = Req(HttpMethod.Get, url, token);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(buf, cts.Token)) > 0) total += n;
        }
        catch (OperationCanceledException) when (total > 0) { /* время теста вышло: считаем по тому, что успели получить */ }
        return Mbps(total, sw.Elapsed);
    }

    private static async Task<double> UploadAsync(string url, string? token, int size)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var data = new byte[size];
        Random.Shared.NextBytes(data);
        var sw = Stopwatch.StartNew();
        using var req = Req(HttpMethod.Post, url, token);
        req.Content = new ByteArrayContent(data);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var resp = await Http.SendAsync(req, cts.Token);
        resp.EnsureSuccessStatusCode();
        return Mbps(size, sw.Elapsed);
    }

    private static double Mbps(long bytes, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : Math.Round(bytes * 8 / 1_000_000.0 / elapsed.TotalSeconds, 1);
}
