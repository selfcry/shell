using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace ClubShell.Core;

/// <summary>
/// Настройки ПК: %ProgramData%\ClubShell\config.json. Читает и пишет только служба (SYSTEM):
/// папка закрыта для обычных пользователей, окно гостя получает нужное через named pipe.
/// PIN админа хранится хешем; открытый PIN при первом запуске кладётся в admin-pin.txt (читать может только админ).
/// </summary>
public sealed class ShellConfig
{
    public string PcName { get; set; } = Environment.MachineName;
    public string Zone { get; set; } = "ARENA";

    /// <summary>Сколько секунд без ответа сервера терпим, прежде чем заблокировать ПК.</summary>
    public int OfflineGraceSec { get; set; } = 30;

    /// <summary>
    /// Контроль сеанса службой: если по данным сервера сеанса нет (или связь потеряна), а окно блокировки
    /// столько секунд не подтверждает, что ПК заблокирован (окно приостановлено, подменено, зависло), —
    /// служба завершает сеанс Windows гостя и перезагружает ПК. 0 — выключено.
    /// </summary>
    public int EnforceLockSec { get; set; } = 15;

    /// <summary>Окно блокировки блокируется само, если служба замолчала на 15 секунд.</summary>
    public bool RequireService { get; set; } = true;

    /// <summary>Учётная запись гостя: её профиль чистится при загрузке ПК. Админская запись сюда попасть не должна.</summary>
    public string GuestUser { get; set; } = "guest";

    /// <summary>Чистить профиль гостя при загрузке Windows (после перезагрузки в конце сеанса).</summary>
    public bool WipeOnBoot { get; set; } = true;

    /// <summary>Подпапки профиля гостя, содержимое которых удаляется. Пусто — берётся список по умолчанию.</summary>
    public List<string> WipeDirs { get; set; } = new();

    /// <summary>Адрес сервера клуба (например https://club.example.ru). Пусто — работает заглушка с тестовыми гостями.</summary>
    public string? ServerUrl { get; set; }

    /// <summary>Токен этого ПК: админ получает его при добавлении ПК в админке и прописывает в config.json.</summary>
    public string? PcToken { get; set; }

    public string AdminPinSalt { get; set; } = "";
    public string AdminPinHash { get; set; } = "";

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubShell");
    public static string FilePath => Path.Combine(Dir, "config.json");
    public static string PinFilePath => Path.Combine(Dir, "admin-pin.txt");

    /// <summary>Если файл существует, служба не перезапускает окно блокировки (режим обслуживания).</summary>
    public static string MaintenanceFlag => Path.Combine(Dir, "maintenance");

    /// <summary>Для проверки: создайте этот файл, и заглушка сервера начнёт «терять связь».</summary>
    public static string SimulateOfflineFlag => Path.Combine(Dir, "simulate-offline");

    public static ShellConfig Load()
    {
        Directory.CreateDirectory(Dir);
        RestrictDirectory();

        ShellConfig cfg;
        try { cfg = JsonSerializer.Deserialize<ShellConfig>(File.ReadAllText(FilePath)) ?? new(); }
        catch { cfg = new(); }

        if (string.IsNullOrEmpty(cfg.AdminPinHash))
        {
            var pin = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            cfg.SetPin(pin);
            File.WriteAllText(PinFilePath, $"PIN администратора: {pin}{Environment.NewLine}Прочитайте и удалите этот файл.{Environment.NewLine}");
            cfg.Save();
        }
        return cfg;
    }

    public void Save() =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public void SetPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        AdminPinSalt = Convert.ToBase64String(salt);
        AdminPinHash = Convert.ToBase64String(Hash(pin, salt));
    }

    public bool VerifyPin(string pin)
    {
        if (string.IsNullOrEmpty(AdminPinHash)) return false;
        var expected = Convert.FromBase64String(AdminPinHash);
        return CryptographicOperations.FixedTimeEquals(Hash(pin, Convert.FromBase64String(AdminPinSalt)), expected);
    }

    private static byte[] Hash(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);

    /// <summary>Папка доступна только SYSTEM и администраторам (на Windows).</summary>
    private static void RestrictDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
        }
        new DirectoryInfo(Dir).SetAccessControl(security);
    }
}
