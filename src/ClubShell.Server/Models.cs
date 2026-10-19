namespace ClubShell.Server;

public record Pc(long Id, string Name, string Zone, long LastSeen);
public record Tariff(string Id, string Name, int Minutes, int Price, bool NightOnly, string Zone, bool Active, int Sort);
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
public record UpdateGuestReq(string? Password, bool? Banned, string? Birth);
public record TopUpReq(int Amount, string? Note, string? RequestId); // RequestId: повтор того же запроса (двойной клик) не проводится второй раз
public record TariffReq(string Id, string Name, int Minutes, int Price, bool NightOnly, string? Zone, bool? Active, int? Sort);
public record AppReq(string? Id, string? Title, string? Kind, string? Group, List<string>? Paths, string? Args, string? WorkDir, bool? ClubAccount, int? MinAge, bool? Enabled, bool? Pinned, int? Sort);
public record BlockedReq(string? Process, string? Note);
public record CommandReq(string Name, string? Text);
public record AddTimeReq(int Minutes);
