using ClubShell.Core;

namespace ClubShell.Locker;

/// <summary>
/// Сервер клуба «через службу»: окно не знает токен ПК, поэтому каждый вызов — запрос по каналу,
/// а к серверу обращается служба (SYSTEM). Ответ fail=true означает «нет связи» и становится HttpRequestException,
/// как у HttpClubApi, — остальной код окна не отличает эти два случая.
/// </summary>
internal sealed class PipeClubApi : IClubApi
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly ServiceLink _link;

    public PipeClubApi(ServiceLink link) => _link = link;

    private async Task<PipeMessage> CallAsync(PipeMessage request, TimeSpan? timeout = null)
    {
        var r = await _link.RequestAsync(request, timeout ?? Timeout);
        if (r.Fail) throw new HttpRequestException(r.Error ?? "Нет связи с сервером");
        return r;
    }

    public async Task<LoginResult> LoginAsync(string pcId, string username, string password)
    {
        var r = await CallAsync(new PipeMessage { Op = "login", User = username, Password = password });
        return new LoginResult(r.Ok, r.Error, r.User, r.Balance, r.Packages);
    }

    public async Task<LoginResult> RegisterAsync(string pcId, string username, string password, string birth)
    {
        var r = await CallAsync(new PipeMessage { Op = "register", User = username, Password = password, Birth = birth });
        return new LoginResult(r.Ok, r.Error, r.User, r.Balance, r.Packages);
    }

    public async Task<CatalogSnapshot?> GetCatalogAsync() => (await CallAsync(new PipeMessage { Op = "catalog" })).Catalog;

    /// <summary>Служба берёт гостя из своего состояния (кто вошёл через неё), имя от окна только для сверки.</summary>
    public async Task<StartResult> StartSessionAsync(string pcId, string username, string packageId)
    {
        var r = await CallAsync(new PipeMessage { Op = "start", User = username, PackageId = packageId });
        return new StartResult(r.Ok, r.Error, r.Balance, r.Minutes);
    }

    public async Task StopSessionAsync(string pcId, string username) => await CallAsync(new PipeMessage { Op = "stop", User = username });

    /// <summary>Пульс шлёт сама служба.</summary>
    public Task<HeartbeatInfo?> HeartbeatAsync(string pcId, string? username) => Task.FromResult<HeartbeatInfo?>(null);

    /// <summary>Гость вышел, не начав сеанс: служба забывает его (пульс уходит без имени).</summary>
    public Task LogoutAsync() => CallAsync(new PipeMessage { Op = "logout" });

    public async Task<SpeedResult> SpeedTestAsync()
    {
        var r = await CallAsync(new PipeMessage { Op = "speedtest" }, TimeSpan.FromSeconds(60));
        return r.Speed ?? new SpeedResult(false, 0, 0, null, r.Error ?? "Нет ответа службы", null);
    }
}
