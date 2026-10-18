using System.Net.Http.Json;
using System.Text.Json;

namespace ClubShell.Core;

/// <summary>Клиент настоящего сервера клуба (ClubShell.Server). Токен ПК выдаётся админом при добавлении ПК и уходит в заголовке X-Pc-Token.</summary>
public sealed class HttpClubApi : IClubApi, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public HttpClubApi(string baseUrl, string pcToken)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(6) };
        _http.DefaultRequestHeaders.Add("X-Pc-Token", pcToken);
    }

    private sealed record LoginDto(bool Ok, string? Error, string? User, int Balance, List<TariffPackage>? Packages);
    private sealed record StartDto(bool Ok, string? Error, int Balance, int Minutes);

    // Любая ошибка транспорта или ответ не 2xx становится HttpRequestException: так окно и служба понимают, что «связи нет».
    private async Task<T> PostAsync<T>(string url, object body)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, Json);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<T>(Json)) ?? throw new HttpRequestException("empty response");
    }

    public async Task<LoginResult> LoginAsync(string pcId, string username, string password)
    {
        var r = await PostAsync<LoginDto>("api/pc/login", new { username, password });
        return new LoginResult(r.Ok, r.Error, r.User, r.Balance, r.Packages);
    }

    public async Task<StartResult> StartSessionAsync(string pcId, string username, string packageId)
    {
        var r = await PostAsync<StartDto>("api/pc/session/start", new { username, packageId });
        return new StartResult(r.Ok, r.Error, r.Balance, r.Minutes);
    }

    public async Task StopSessionAsync(string pcId, string username) =>
        await PostAsync<JsonElement>("api/pc/session/stop", new { username });

    public async Task HeartbeatAsync(string pcId, string? username) =>
        await PostAsync<JsonElement>("api/pc/heartbeat", new { username });

    public void Dispose() => _http.Dispose();
}
