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

    private sealed record LoginDto(bool Ok, string? Error, string? User, int Balance, List<TariffPackage>? Packages, int Bonus, LoyaltyInfo? Loyalty);
    private sealed record StartDto(bool Ok, string? Error, int Balance, int Minutes, int Bonus);
    private sealed record PromoDto(bool Ok, string? Error, int Bonus);

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
        return new LoginResult(r.Ok, r.Error, r.User, r.Balance, r.Packages, r.Bonus, r.Loyalty);
    }

    public async Task<LoginResult> RegisterAsync(string pcId, string username, string password, string birth)
    {
        var r = await PostAsync<LoginDto>("api/pc/register", new { username, password, birth });
        return new LoginResult(r.Ok, r.Error, r.User, r.Balance, r.Packages);
    }

    private sealed record CatalogDto(long Version, List<ServerCatalogItem>? Items, List<string>? Blocked);

    public string BaseUrl => _http.BaseAddress!.ToString().TrimEnd('/');

    public async Task<CatalogSnapshot?> GetCatalogAsync()
    {
        using var resp = await _http.GetAsync("api/pc/catalog");
        resp.EnsureSuccessStatusCode();
        var d = await resp.Content.ReadFromJsonAsync<CatalogDto>(Json) ?? throw new HttpRequestException("empty response");
        return new CatalogSnapshot(d.Version, d.Items ?? new(), d.Blocked ?? new());
    }

    public async Task<StartResult> StartSessionAsync(string pcId, string username, string packageId)
    {
        var r = await PostAsync<StartDto>("api/pc/session/start", new { username, packageId });
        return new StartResult(r.Ok, r.Error, r.Balance, r.Minutes, r.Bonus);
    }

    public async Task StopSessionAsync(string pcId, string username) =>
        await PostAsync<JsonElement>("api/pc/session/stop", new { username });

    private sealed record HeartbeatSessionDto(string? User, int RemainingSec);
    private sealed record HeartbeatDto(bool Ok, HeartbeatSessionDto? Session);

    public async Task<HeartbeatInfo?> HeartbeatAsync(string pcId, string? username)
    {
        var r = await PostAsync<HeartbeatDto>("api/pc/heartbeat", new { username });
        return r.Session is { } s ? new HeartbeatInfo(s.RemainingSec > 0, s.RemainingSec, s.User) : new HeartbeatInfo(false, 0, null);
    }

    public async Task<bool> CallAdminAsync(string? username) => (await PostAsync<JsonElement>("api/pc/call", new { username })).GetProperty("ok").GetBoolean();

    public async Task<PromoResult> RedeemPromoAsync(string username, string code)
    {
        var r = await PostAsync<PromoDto>("api/pc/promo", new { username, code });
        return new PromoResult(r.Ok, r.Error, r.Bonus);
    }

    public void Dispose() => _http.Dispose();
}
