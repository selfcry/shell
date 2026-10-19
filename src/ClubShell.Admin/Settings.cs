using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClubShell.Admin;

/// <summary>
/// Адрес сервера и ключ администратора. Лежат в %AppData%\ClubShell.Admin\settings.json;
/// ключ зашифрован DPAPI (расшифровать может только этот пользователь Windows на этом компьютере).
/// В интерфейс (веб-страницу) ключ не передаётся вообще: запросы к серверу делает хост.
/// </summary>
internal sealed class Settings
{
    public string ServerUrl { get; set; } = "https://cryvx.ru";
    public string KeyProtected { get; set; } = "";

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClubShell.Admin", "settings.json");

    public bool HasKey => KeyProtected.Length > 0;

    public string? GetKey()
    {
        if (!HasKey) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(KeyProtected), null, DataProtectionScope.CurrentUser)); }
        catch (Exception) { return null; }
    }

    public void SetKey(string key) =>
        KeyProtected = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
