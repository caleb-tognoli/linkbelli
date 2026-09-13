using System.Security.Cryptography;
using System.Text;

namespace Linkbelli.Application.Auth;

/// <summary>
/// The secret in an invitation link.
/// </summary>
/// <remarks>
/// Hashed on the way in, exactly as an API key is: the plaintext is shown once, when the link is
/// made, and is never recoverable afterwards. A table of live invitation tokens is a table of ways
/// into other people's playlists, and a stolen database backup should not be one.
///
/// Plain SHA-256 rather than a password hash, for the same reason as the API key: this is a
/// 256-bit random secret, not something a person chose, so there is no dictionary to run.
/// </remarks>
public static class InviteToken
{
    private const int SecretBytes = 32;

    /// <summary>A fresh token and its hash. Keep the hash; hand over the token once.</summary>
    public static (string Token, string Hash) Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return (token, HashOf(token));
    }

    public static string HashOf(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
