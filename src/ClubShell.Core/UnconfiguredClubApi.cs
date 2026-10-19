namespace ClubShell.Core;

/// <summary>
/// Боевой ПК, которому служба ещё не передала адрес сервера и токен (или они не заданы): вход запрещён.
/// В отличие от FakeClubApi, никаких тестовых гостей — иначе первые секунды после загрузки можно было бы играть бесплатно.
/// </summary>
public sealed class UnconfiguredClubApi : IClubApi
{
    private const string Message = "ПК не подключён к серверу клуба. Обратитесь к администратору.";

    public Task<LoginResult> LoginAsync(string pcId, string username, string password) => Task.FromResult(new LoginResult(false, Message));
    public Task<LoginResult> RegisterAsync(string pcId, string username, string password, string birth) => Task.FromResult(new LoginResult(false, Message));
    public Task<CatalogSnapshot?> GetCatalogAsync() => Task.FromResult<CatalogSnapshot?>(null);
    public Task<StartResult> StartSessionAsync(string pcId, string username, string packageId) => Task.FromResult(new StartResult(false, Message));
    public Task StopSessionAsync(string pcId, string username) => Task.CompletedTask;
    public Task HeartbeatAsync(string pcId, string? username) => Task.CompletedTask;
}
