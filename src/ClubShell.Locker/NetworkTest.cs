using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ClubShell.Locker;

internal sealed record SpeedResult(bool Ok, double DownMbps, double UpMbps, long? PingMs, string? Error);

/// <summary>Пинг до заданного узла и тест скорости (скачивание и отдача) через speed.cloudflare.com.</summary>
internal static class NetworkTest
{
    /// <summary>Узел для пинга: по умолчанию публичный DNS; потом заменим адресом клубного сервера.</summary>
    public static string PingHost => Environment.GetEnvironmentVariable("CLUBSHELL_PING_HOST") ?? "1.1.1.1";

    private static string BaseUrl => Environment.GetEnvironmentVariable("CLUBSHELL_SPEEDTEST_URL") ?? "https://speed.cloudflare.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static int _running;

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

    /// <summary>Не больше ~8 секунд на каждое направление, чтобы тест не занимал канал клуба надолго.</summary>
    public static async Task<SpeedResult> RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return new SpeedResult(false, 0, 0, null, "Тест уже идёт");
        try
        {
            var ping = await PingAsync();
            var down = await DownloadAsync();
            var up = await UploadAsync();
            return new SpeedResult(true, down, up, ping, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ClubShell] speedtest failed: {ex.Message}");
            return new SpeedResult(false, 0, 0, null, "Не удалось измерить скорость");
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private static async Task<double> DownloadAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var sw = Stopwatch.StartNew();
        long total = 0;
        try
        {
            using var resp = await Http.GetAsync($"{BaseUrl}/__down?bytes=100000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            var buf = new byte[81920];
            int n;
            while ((n = await stream.ReadAsync(buf, cts.Token)) > 0) total += n;
        }
        catch (OperationCanceledException) { /* вышло время теста: считаем по тому, что успели получить */ }
        return Mbps(total, sw.Elapsed);
    }

    private static async Task<double> UploadAsync()
    {
        const int size = 10_000_000;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var sw = Stopwatch.StartNew();
        try
        {
            using var content = new ByteArrayContent(new byte[size]);
            using var resp = await Http.PostAsync($"{BaseUrl}/__up", content, cts.Token);
            resp.EnsureSuccessStatusCode();
            return Mbps(size, sw.Elapsed);
        }
        catch (OperationCanceledException) { return 0; }
    }

    private static double Mbps(long bytes, TimeSpan elapsed) =>
        elapsed.TotalSeconds <= 0 ? 0 : Math.Round(bytes * 8 / 1_000_000.0 / elapsed.TotalSeconds, 1);
}
