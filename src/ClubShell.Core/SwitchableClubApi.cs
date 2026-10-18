namespace ClubShell.Core;

/// <summary>Обёртка, у которой можно поменять реализацию на лету: окно стартует с заглушкой, а настоящий сервер подключает, когда служба пришлёт адрес и токен.</summary>
public sealed class SwitchableClubApi : IClubApi
{
    private volatile IClubApi _inner;
    public SwitchableClubApi(IClubApi inner) => _inner = inner;
    public IClubApi Inner { get => _inner; set => _inner = value; }

    public Task<LoginResult> LoginAsync(string pcId, string u, string p) => _inner.LoginAsync(pcId, u, p);
    public Task<StartResult> StartSessionAsync(string pcId, string u, string pkg) => _inner.StartSessionAsync(pcId, u, pkg);
    public Task StopSessionAsync(string pcId, string u) => _inner.StopSessionAsync(pcId, u);
    public Task HeartbeatAsync(string pcId, string? u) => _inner.HeartbeatAsync(pcId, u);
}
