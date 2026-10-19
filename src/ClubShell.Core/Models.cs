namespace ClubShell.Core;

/// <summary>Пакет тарифа (как на mirage-ptg.ru/prices: 1/3/5 часов и ночь).</summary>
public record TariffPackage(string Id, string Name, int Minutes, int Price, bool NightOnly = false);

public record LoginResult(bool Ok, string? Error = null, string? User = null, int Balance = 0,
    IReadOnlyList<TariffPackage>? Packages = null, int Bonus = 0, LoyaltyInfo? Loyalty = null);

/// <summary>Уровень гостя в программе лояльности (бонусы, кешбэк, сколько до следующего уровня).</summary>
public record LoyaltyInfo(int Bonus, int Spent, string? Level, int CashbackPct, string? NextLevel, int ToNext);

/// <summary>Результат ввода промокода: новое количество бонусов.</summary>
public record PromoResult(bool Ok, string? Error = null, int Bonus = 0);

/// <summary>Каталог игр и приложений с сервера клуба (то, что админ настроил в админ-панели).</summary>
public record ServerCatalogItem(string Id, string Title, string Kind, string? Group, string? Cover, List<string> Paths, string? Args, string? WorkDir,
    bool ClubAccount, int? MinAge, bool Pinned);

public record CatalogSnapshot(long Version, List<ServerCatalogItem> Items, List<string> Blocked);

/// <summary>Что сервер знает о сеансе на этом ПК (ответ на пульс).</summary>
public record HeartbeatInfo(bool SessionActive, int RemainingSec, string? User);

public record StartResult(bool Ok, string? Error = null, int Balance = 0, int Minutes = 0, int Bonus = 0);
