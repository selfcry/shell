using ClubShell.Core;

namespace ClubShell.Locker;

/// <summary>
/// Пинг и тест скорости для панели задач. На боевом ПК тест до сервера клуба выполняет служба (у окна нет токена ПК),
/// в тестовом запуске без службы — само окно с CLUBSHELL_SERVER_URL + CLUBSHELL_PC_TOKEN.
/// </summary>
internal static class NetworkTest
{
    private static string? _serverUrl;
    private static string? _token;
    private static Func<Task<SpeedResult>>? _remote;
    private static int _running;

    /// <summary>Тестовый запуск без службы: окно само меряет скорость до сервера.</summary>
    public static void Configure(string? serverUrl, string? token)
    {
        _serverUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/');
        _token = token;
    }

    /// <summary>Боевой ПК: адрес сервера (для пинга) от службы, сам тест — запросом к службе.</summary>
    public static void UseService(string? serverUrl, Func<Task<SpeedResult>> remote)
    {
        Configure(serverUrl, null);
        _remote = remote;
    }

    public static string PingHost => DevFlags.Get("CLUBSHELL_PING_HOST") is { Length: > 0 } env ? env : SpeedTest.PingHost(_serverUrl);

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    public static Task<long?> PingAsync() => SpeedTest.PingAsync(PingHost);

    public static async Task<SpeedResult> RunAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return new SpeedResult(false, 0, 0, null, "Тест уже идёт", null);
        try
        {
            if (_remote is { } remote)
            {
                try { return await remote(); }
                catch (Exception ex) { return new SpeedResult(false, 0, 0, null, "Не удалось измерить: " + ex.Message, null); }
            }
            return await SpeedTest.RunAsync(_serverUrl, _token, PingHost);
        }
        finally { Volatile.Write(ref _running, 0); }
    }
}
