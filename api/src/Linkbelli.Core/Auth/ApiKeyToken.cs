using System.Security.Cryptography;
using System.Text;

namespace Linkbelli.Core.Auth;

/// <summary>
/// Generation, parsing and hashing of API key tokens. Token format is
/// <c>lbk_{publicId}_{secret}</c>: the publicId is stored for indexed lookup,
/// the secret is high-entropy (256-bit) so a plain SHA-256 is sufficient — no
/// password-style slow hashing needed.
/// </summary>
public static class ApiKeyToken
{
    public const string HeaderName = "X-Api-Key";
    public const string TokenPrefix = "lbk";
    private const int PublicIdBytes = 9;  // -> 12 base64url chars
    private const int SecretBytes = 32;   // 256-bit secret

    public static GeneratedApiKey Generate()
    {
        // publicId is hex (no '_') so it never collides with the '_' delimiter;
        // the secret is base64url and may contain '_', so it must be the last segment.
        var publicId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PublicIdBytes));
        var secret = ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        return new GeneratedApiKey($"{TokenPrefix}_{publicId}_{secret}", publicId, Hash(secret));
    }

    public static bool TryParse(string? token, out string publicId, out string secret)
    {
        publicId = secret = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        // Cap at 3 so the secret keeps any '_' it contains; publicId is hex, so the
        // second '_' is always the true delimiter.
        var parts = token.Split('_', 3);
        if (parts.Length != 3 || parts[0] != TokenPrefix || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return false;
        }

        publicId = parts[1];
        secret = parts[2];
        return true;
    }

    public static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The one-time result of generating a key: full token to show the user, plus what to persist.</summary>
public readonly record struct GeneratedApiKey(string Token, string PublicId, string Hash);
