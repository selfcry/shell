namespace ClubShell.Core;

/// <summary>
/// Граница с сервером клуба. Сейчас реализована заглушкой (FakeClubApi);
/// позже сюда подключается настоящий сервер (HTTP + WebSocket) без правок остального кода.
/// </summary>
public interface IClubApi
{
    Task<LoginResult> LoginAsync(string pcId, string username, string password);
    /// <summary>Регистрация, шаг 1: проверка данных и отправка SMS-кода на телефон (birth — yyyy-MM-dd).</summary>
    Task<CodeResult> StartRegistrationAsync(string pcId, string username, string password, string birth, string phone);

    /// <summary>Регистрация, шаг 2: код из SMS. При успехе ответ такой же, как у входа.</summary>
    Task<LoginResult> ConfirmRegistrationAsync(string pcId, string phone, string code);
    Task<StartResult> StartSessionAsync(string pcId, string username, string packageId);
    Task StopSessionAsync(string pcId, string username);

    /// <summary>Пульс активности ПК (раз в 5 с): если сервер долго его не получает, ПК считается потерянным.</summary>
    Task HeartbeatAsync(string pcId, string? username);
}
