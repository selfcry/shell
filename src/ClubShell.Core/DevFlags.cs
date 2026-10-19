using Microsoft.Win32;

namespace ClubShell.Core;

/// <summary>
/// Переменные окружения для разработки и тестов (CLUBSHELL_TEST, CLUBSHELL_DEV_URL, CLUBSHELL_CATALOG…).
/// Окружение гостя он может менять сам (HKCU\Environment), поэтому на боевом ПК — где установлена служба —
/// они игнорируются полностью. Работают только на машине без службы: у разработчика и на тестовой виртуалке.
/// </summary>
public static class DevFlags
{
    private static readonly Lazy<bool> Provisioned = new(() =>
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ClubShellService");
            return key is not null;
        }
        catch (Exception) { return true; } // не смогли проверить — считаем боевым ПК
    });

    /// <summary>На этом ПК установлена служба-страж (боевой режим).</summary>
    public static bool IsProductionPc => Provisioned.Value;

    public static string? Get(string name) => IsProductionPc ? null : Environment.GetEnvironmentVariable(name);
}
