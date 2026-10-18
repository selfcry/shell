namespace ClubShell.Core;

/// <summary>Пакет тарифа (как на mirage-ptg.ru/prices: 1/3/5 часов и ночь).</summary>
public record TariffPackage(string Id, string Name, int Minutes, int Price, bool NightOnly = false);

public record LoginResult(bool Ok, string? Error = null, string? User = null, int Balance = 0,
    IReadOnlyList<TariffPackage>? Packages = null);

public record StartResult(bool Ok, string? Error = null, int Balance = 0, int Minutes = 0);
