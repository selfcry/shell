using System.Collections.Concurrent;
using System.Net.Http.Json;

namespace ClubShell.Server;

public sealed record ClientManifest(string Version, string Url, string Sha256, string Signature, long Size);

/// <summary>
/// Какую версию клиента ставить на ПК. Манифест берётся из релиза GitHub (его собирает и подписывает CI),
/// кэшируется на 10 минут. Подпись проверяет сама служба на ПК, сервер её только передаёт.
/// Настройки (админка → «Настройки»): включено ли, режим «только тестовые ПК» и их список.
/// </summary>
public sealed class UpdateFeed
{
    public const string DefaultUrl = "https://github.com/selfcry/shell/releases/latest/download/client-manifest.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ClubService _club;
    private readonly ILogger<UpdateFeed> _log;
    private (DateTime at, ClientManifest? m, string? error) _cache;
    private readonly ConcurrentDictionary<long, string> _pcVersions = new();

    public UpdateFeed(ClubService club, ILogger<UpdateFeed> log) => (_club, _log) = (club, log);

    public bool Enabled => _club.Setting("upd_enabled") == "1";
    public string Ring => _club.Setting("upd_ring") ?? "";            // имена тестовых ПК через запятую
    public bool TestOnly => _club.Setting("upd_mode") != "all";        // по умолчанию — только тестовые ПК
    public string Url => _club.Setting("upd_url") is { Length: > 0 } u ? u : DefaultUrl;

    public void SeenVersion(long pcId, string? version)
    {
        if (version is { Length: > 0 and < 32 }) _pcVersions[pcId] = version;
    }

    public string? VersionOf(long pcId) => _pcVersions.GetValueOrDefault(pcId);

    public async Task<(ClientManifest? manifest, string? error)> LatestAsync(bool force = false)
    {
        if (!force && DateTime.UtcNow - _cache.at < TimeSpan.FromMinutes(10)) return (_cache.m, _cache.error);
        try
        {
            var m = await Http.GetFromJsonAsync<ClientManifest>(Url);
            _cache = (DateTime.UtcNow, m, m is null ? "Пустой манифест" : null);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Манифест обновления недоступен: {Error}", ex.Message);
            _cache = (DateTime.UtcNow, _cache.m, "Не удалось получить манифест: " + ex.Message);
        }
        return (_cache.m, _cache.error);
    }

    /// <summary>Манифест для этого ПК или null (обновлять не нужно, выключено, ПК не в тестовой группе).</summary>
    public async Task<ClientManifest?> ForPcAsync(Pc pc, string currentVersion)
    {
        if (!Enabled) return null;
        if (TestOnly && !Ring.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(pc.Name, StringComparer.OrdinalIgnoreCase)) return null;
        var (m, _) = await LatestAsync();
        if (m is null || !Version.TryParse(m.Version, out var latest) || !Version.TryParse(currentVersion, out var cur) || latest <= cur) return null;
        return m;
    }
}
