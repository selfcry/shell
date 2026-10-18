namespace ClubShell.Core;

/// <summary>Заглушка сервера: гости и тарифы в памяти. Тарифы зоны ARENA «Мираж PRO».</summary>
public sealed class FakeClubApi : IClubApi
{
    private static readonly TariffPackage[] Packages =
    {
        new("h1", "1 час", 60, 140),
        new("h3", "3 часа", 180, 350),
        new("h5", "5 часов", 300, 500),
        new("night", "Ночь", 600, 550, NightOnly: true),
    };

    private readonly Dictionary<string, (string Password, int Balance)> _users = new()
    {
        ["demo"] = ("demo", 600),
        ["vip"] = ("vip", 2000),
    };

    /// <summary>Имитация обрыва связи: пока существует файл simulate-offline, все вызовы падают.</summary>
    private static void ThrowIfOffline()
    {
        if (File.Exists(ShellConfig.SimulateOfflineFlag)) throw new HttpRequestException("simulated outage");
    }

    public Task<LoginResult> LoginAsync(string pcId, string username, string password)
    {
        ThrowIfOffline();
        if (!_users.TryGetValue(username, out var u) || u.Password != password)
            return Task.FromResult(new LoginResult(false, "Неверный логин или пароль"));
        return Task.FromResult(new LoginResult(true, null, username, u.Balance, Packages));
    }

    public Task<StartResult> StartSessionAsync(string pcId, string username, string packageId)
    {
        ThrowIfOffline();
        var p = Packages.FirstOrDefault(x => x.Id == packageId);
        if (p is null) return Task.FromResult(new StartResult(false, "Тариф не найден"));
        if (p.NightOnly && !IsNight(DateTime.Now)) return Task.FromResult(new StartResult(false, "Ночной тариф с 22:00 до 08:00"));
        if (!_users.TryGetValue(username, out var u)) return Task.FromResult(new StartResult(false, "Гость не найден"));
        if (u.Balance < p.Price) return Task.FromResult(new StartResult(false, "Недостаточно средств"));
        _users[username] = (u.Password, u.Balance - p.Price);
        return Task.FromResult(new StartResult(true, null, u.Balance - p.Price, p.Minutes));
    }

    public Task StopSessionAsync(string pcId, string username) { ThrowIfOffline(); return Task.CompletedTask; }
    public Task HeartbeatAsync(string pcId, string? username) { ThrowIfOffline(); return Task.CompletedTask; }

    private static bool IsNight(DateTime t) => t.Hour >= 22 || t.Hour < 8;
}
