namespace ClubShell.Core;

/// <summary>
/// Граница с сервером клуба. Сейчас реализована заглушкой (FakeClubApi);
/// позже сюда подключается настоящий сервер (HTTP + WebSocket) без правок остального кода.
/// </summary>
public interface IClubApi
{
    Task<LoginResult> LoginAsync(string pcId, string username, string password);
    Task<StartResult> StartSessionAsync(string pcId, string username, string packageId);
    Task StopSessionAsync(string pcId, string username);

    /// <summary>Пульс активности ПК (раз в 5 с): если сервер долго его не получает, ПК считается потерянным.</summary>
    Task HeartbeatAsync(string pcId, string? username);
}
