using System.Security.Cryptography;
using System.Text;

namespace ClubShell.Server;

public static class Security
{
    /// <summary>Пароль гостя: PBKDF2-SHA256, 100 000 итераций, соль 16 байт. Формат «соль:хеш» (base64).</summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Pbkdf2(password, salt));
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        try { return CryptographicOperations.FixedTimeEquals(Pbkdf2(password, Convert.FromBase64String(parts[0])), Convert.FromBase64String(parts[1])); }
        catch (FormatException) { return false; }
    }

    private static byte[] Pbkdf2(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    /// <summary>Случайный токен (для ПК и админа). В базе хранится только его SHA-256.</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
