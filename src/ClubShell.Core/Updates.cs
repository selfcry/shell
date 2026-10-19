using System.Security.Cryptography;
using System.Text;

namespace ClubShell.Core;

/// <summary>Описание версии клиента (служба + оболочка): что скачать и чем подписано.</summary>
public sealed record UpdateManifest(string Version, string Url, string Sha256, string Signature, long Size = 0);

/// <summary>
/// Проверка обновлений клиента. Пакет подписывает CI закрытым ключом ECDSA P-256 (секрет репозитория);
/// здесь вшит открытый ключ. Подписывается строка «версия\nsha256» — подменить файл или версию без ключа нельзя,
/// даже если кто-то перехватит сервер клуба или GitHub.
/// </summary>
public static class UpdateVerifier
{
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEv2/lNcAe8DxwMkRyfzJ3E+AXfFEm
        1rU0+GQAHUpkuMML7QIpsMRCZHQHNJa/vfgoX6TmH/GvwTlV+5ZrUWX1sQ==
        -----END PUBLIC KEY-----
        """;

    public static string SignedText(string version, string sha256) => $"{version}\n{sha256.ToLowerInvariant()}";

    public static bool SignatureValid(UpdateManifest m)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);
            var sig = Convert.FromBase64String(m.Signature);
            return ecdsa.VerifyData(Encoding.UTF8.GetBytes(SignedText(m.Version, m.Sha256)), sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception) { return false; }
    }

    public static string Sha256Of(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    /// <summary>Новее ли версия a, чем b (1.0.12 &gt; 1.0.9).</summary>
    public static bool IsNewer(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) && va > vb;

    public static string CurrentVersion => typeof(UpdateVerifier).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}
