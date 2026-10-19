using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClubShell.Server;

public sealed record ClientManifest(string Version, string Url, string Sha256, string Signature, long Size);

/// <summary>
/// Обновления клиента для ПК. Репозиторий приватный, поэтому ПК ничего не качают с GitHub сами:
/// сервер клуба раз в 10 минут спрашивает GitHub API (токен только на чтение, задаётся в админке) про последний релиз,
/// скачивает подписанный манифест и пакет к себе (/var/lib/clubshell/updates) и сверяет SHA-256,
/// а ПК получают пакет с cryvx.ru (/api/pc/update/package) по своему токену. Подпись пакета проверяет служба на ПК —
/// она подписывает «версия + SHA-256», поэтому адрес скачивания сервер может подставлять свой.
/// Настройки: включено ли, режим «только тестовые ПК» и их список, токен и репозиторий GitHub.
/// </summary>
public sealed class UpdateFeed
{
    public const string DefaultRepo = "selfcry/shell";
    private const string ManifestAsset = "client-manifest.json";
    private const string PackageAsset = "ClubShell.Client-win-x64.zip";
    private const long MaxPackage = 400L * 1024 * 1024;

    private static readonly HttpClient Http = CreateHttp();
    private readonly ClubService _club;
    private readonly ILogger<UpdateFeed> _log;
    private readonly string _dir;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private (DateTime at, ClientManifest? m, string? error) _cache;
    private readonly ConcurrentDictionary<long, string> _pcVersions = new();

    public UpdateFeed(ClubService club, IConfiguration cfg, ILogger<UpdateFeed> log)
    {
        (_club, _log) = (club, log);
        _dir = Path.Combine(cfg["Club:DataDir"] ?? Path.Combine(AppContext.BaseDirectory, "data"), "updates");
    }

    private static HttpClient CreateHttp()
    {
        // При переадресации на хранилище GitHub заголовок Authorization .NET не пересылает — так и нужно.
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ClubShell-Server");
        h.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return h;
    }

    public bool Enabled => _club.Setting("upd_enabled") == "1";
    public string Ring => _club.Setting("upd_ring") ?? "";            // имена тестовых ПК через запятую
    public bool TestOnly => _club.Setting("upd_mode") != "all";        // по умолчанию — только тестовые ПК
    public string Repo => _club.Setting("gh_repo") is { Length: > 0 } r ? r : DefaultRepo;
    public bool HasToken => !string.IsNullOrEmpty(_club.Setting("gh_token"));

    public void SeenVersion(long pcId, string? version)
    {
        if (version is { Length: > 0 and < 32 }) _pcVersions[pcId] = version;
    }

    public string? VersionOf(long pcId) => _pcVersions.GetValueOrDefault(pcId);

    /// <summary>Файл пакета этой версии на сервере (если уже скачан и сверен).</summary>
    public string? PackagePath(string version)
    {
        if (!Version.TryParse(version, out _)) return null;
        var f = Path.Combine(_dir, $"client-{version}.zip");
        return File.Exists(f) ? f : null;
    }

    private HttpRequestMessage Get(string url, bool binary = false)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _club.Setting("gh_token"));
        req.Headers.Accept.ParseAdd(binary ? "application/octet-stream" : "application/vnd.github+json");
        return req;
    }

    /// <summary>Последняя версия: сверяется с GitHub не чаще раза в 10 минут; пакет докачивается на сервер.</summary>
    public async Task<(ClientManifest? manifest, string? error)> LatestAsync(bool force = false)
    {
        if (!force && DateTime.UtcNow - _cache.at < TimeSpan.FromMinutes(10)) return (_cache.m, _cache.error);
        if (!await _sync.WaitAsync(0)) return (_cache.m, _cache.error); // уже проверяем в другом запросе
        try
        {
            if (!HasToken) { _cache = (DateTime.UtcNow, _cache.m, "Не задан токен GitHub (админка → Настройки → Обновления)"); return (_cache.m, _cache.error); }

            using var rel = await Http.SendAsync(Get($"https://api.github.com/repos/{Repo}/releases/latest"));
            if (!rel.IsSuccessStatusCode)
            {
                var why = rel.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "токен GitHub неверный или истёк",
                    System.Net.HttpStatusCode.NotFound => $"нет доступа к {Repo} или в нём нет релизов",
                    _ => $"GitHub ответил {(int)rel.StatusCode}",
                };
                _cache = (DateTime.UtcNow, _cache.m, "Не удалось получить релиз: " + why);
                return (_cache.m, _cache.error);
            }
            using var doc = JsonDocument.Parse(await rel.Content.ReadAsStringAsync());
            string? Asset(string name) => doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == name) is { ValueKind: JsonValueKind.Object } a ? a.GetProperty("url").GetString() : null;
            var manifestUrl = Asset(ManifestAsset);
            var packageUrl = Asset(PackageAsset);
            if (manifestUrl is null || packageUrl is null)
            {
                _cache = (DateTime.UtcNow, _cache.m, "В последнем релизе нет подписанного манифеста или пакета");
                return (_cache.m, _cache.error);
            }

            using var mr = await Http.SendAsync(Get(manifestUrl, binary: true));
            mr.EnsureSuccessStatusCode();
            var m = await mr.Content.ReadFromJsonAsync<ClientManifest>();
            if (m is null || !Version.TryParse(m.Version, out _) || m.Sha256 is not { Length: 64 }) throw new InvalidDataException("плохой манифест");

            // пакет — к себе, один раз на версию; сверяем SHA-256 с манифестом
            Directory.CreateDirectory(_dir);
            var file = Path.Combine(_dir, $"client-{m.Version}.zip");
            if (!File.Exists(file))
            {
                var tmp = file + ".part";
                using (var pr = await Http.SendAsync(Get(packageUrl, binary: true), HttpCompletionOption.ResponseHeadersRead))
                {
                    pr.EnsureSuccessStatusCode();
                    if (pr.Content.Headers.ContentLength > MaxPackage) throw new InvalidDataException("пакет слишком большой");
                    await using var src = await pr.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(tmp);
                    await src.CopyToAsync(dst);
                }
                string hash;
                await using (var fs = File.OpenRead(tmp)) hash = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
                if (!hash.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); throw new InvalidDataException("SHA-256 пакета не совпадает с манифестом"); }
                File.Move(tmp, file, overwrite: true);
                _log.LogWarning("Пакет клиента {Version} скачан на сервер", m.Version);
                // старые пакеты: оставляем три последних
                foreach (var old in new DirectoryInfo(_dir).GetFiles("client-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(3)) old.Delete();
            }
            _cache = (DateTime.UtcNow, m, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Обновление клиента: {Error}", ex.Message);
            _cache = (DateTime.UtcNow, _cache.m, "Не удалось получить обновление: " + ex.Message);
        }
        finally { _sync.Release(); }
        return (_cache.m, _cache.error);
    }

    /// <summary>Манифест для этого ПК (с адресом скачивания на сервере клуба) или null.</summary>
    public async Task<ClientManifest?> ForPcAsync(Pc pc, string currentVersion, string publicBase)
    {
        if (!Enabled) return null;
        if (TestOnly && !Ring.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(pc.Name, StringComparer.OrdinalIgnoreCase)) return null;
        var (m, _) = await LatestAsync();
        if (m is null || !Version.TryParse(m.Version, out var latest) || !Version.TryParse(currentVersion, out var cur) || latest <= cur) return null;
        if (PackagePath(m.Version) is null) return null; // пакет ещё не на сервере
        return m with { Url = $"{publicBase}/api/pc/update/package?version={Uri.EscapeDataString(m.Version)}" };
    }
}
