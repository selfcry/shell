namespace ClubShell.Server;

public record Pc(long Id, string Name, string Zone, long LastSeen);
/// <summary>Тариф. FromHour/ToHour — окно действия по времени клуба (например 8→14 «утро», 22→8 «ночь»); null — круглосуточно.</summary>
public record Tariff(string Id, string Name, int Minutes, int Price, bool NightOnly, string Zone, bool Active, int Sort, int? FromHour = null, int? ToHour = null)
{
    /// <summary>Окно действия: ночной тариф без явного окна — это 22→8.</summary>
    public (int from, int to)? Window => FromHour is { } f && ToHour is { } t ? (f, t) : NightOnly ? (22, 8) : null;
}
public record Guest(long Id, string Login, int Balance, string? Birth, bool Banned, long Created);
public record SessionInfo(long Id, long GuestId, string Guest, long PcId, string Pc, string TariffId, long Started, long Ends, long? Ended, int Price, string? Reason);

// запросы клиентов ПК
public record LoginReq(string Username, string Password);
public record RegisterReq(string Username, string Password, string? Birth);
public record StartReq(string Username, string PackageId);
public record StopReq(string Username);
public record HeartbeatReq(string? Username);

// админ
public record NewPcReq(string Name, string? Zone);
public record NewGuestReq(string Login, string Password, int? Balance, string? Birth);
public record UpdateGuestReq(string? Password, bool? Banned, string? Birth, string? Phone);
public record TopUpReq(int Amount, string? Note, string? RequestId, string? Method); // RequestId: повтор того же запроса (двойной клик) не проводится второй раз
public record TariffReq(string Id, string Name, int Minutes, int Price, bool NightOnly, string? Zone, bool? Active, int? Sort, int? FromHour = null, int? ToHour = null);
public record AppReq(string? Id, string? Title, string? Kind, string? Group, List<string>? Paths, string? Args, string? WorkDir, bool? ClubAccount, int? MinAge, bool? Enabled, bool? Pinned, int? Sort);
public record BlockedReq(string? Process, string? Note);
public record CommandReq(string Name, string? Text);
public record AddTimeReq(int Minutes);

// касса, лояльность, брони, вызовы
public record CallReq(string? Username);
public record PromoReq(string Username, string Code);
public record ShiftOpenReq(string Cashier, int Cash);
public record ShiftCloseReq(int Cash, string? Note);
public record LevelReq(string Name, int MinSpent, int CashbackPct);
public record PromoUpsertReq(string Code, int Bonus, int? MaxUses, long? Expires, bool? Active);
public record BonusReq(int Amount, string? Note);
public record BookingReq(long PcId, string Login, long Starts, int Minutes, string? Note);
public record TelegramReq(string? Token, string? Chat);
public record ProductReq(long? Id, string Name, string? Category, int Price, int? Stock, bool? Active, int? Sort);
public record StockReq(int Qty);
public record SaleItem(long ProductId, int Qty);
public record SaleReq(List<SaleItem> Items, string Method, string? Login);
public record PcOrderReq(string Username, List<SaleItem> Items);
public record OperatorLoginReq(string Login, string Password);
public record OperatorReq(string Login, string? Name, string Password, string Role);
public record OperatorUpdateReq(string? Password, bool? Active, string? Role, string? Name);

// кабинет гостя
public record WebRegisterReq(string Login, string Password, string? Birth, string? Phone);
public record WebLoginReq(string Login, string Password);
public record WebPasswordReq(string OldPassword, string NewPassword);
public record WebBookingReq(long PcId, long Starts, int Minutes);
public record WebPromoReq(string Code);
public record WebPayReq(int Amount);
public record YooKassaReq(string? ShopId, string? Secret, bool? Receipt, int? Vat);
