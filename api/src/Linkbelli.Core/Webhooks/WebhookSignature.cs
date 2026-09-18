using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Linkbelli.Core.Webhooks;

/// <summary>
/// How a receiver knows a delivery came from this Linkbelli and was not replayed.
/// </summary>
/// <remarks>
/// An HMAC over the timestamp and the body, sent as <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>.
/// The same shape Stripe and others use, on purpose: a receiver that has verified one of those
/// already has the code, and every webhook library in every language already knows it.
///
/// The timestamp is inside the signature so a captured delivery cannot be resent later with a
/// fresh date on it; a receiver that rejects anything older than a few minutes has replay
/// protection for the price of one comparison. The <c>v1</c> label leaves room to change the
/// scheme without every existing receiver breaking on the day it happens.
/// </remarks>
public static class WebhookSignature
{
    public const string Header = "Linkbelli-Signature";

    public const string Scheme = "v1";

    /// <summary>The prefix on every secret, so one pasted into the wrong place is recognisable.</summary>
    public const string SecretPrefix = "whsec_";

    /// <summary>A new signing secret.</summary>
    public static string NewSecret() =>
        SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The header value for a body sent at <paramref name="at"/>.</summary>
    public static string Sign(string secret, string body, DateTimeOffset at)
    {
        var timestamp = at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        return $"t={timestamp},{Scheme}={Compute(secret, timestamp, body)}";
    }

    /// <summary>
    /// Whether a header is a valid signature of this body, made within <paramref name="tolerance"/>
    /// of <paramref name="now"/>. What a receiver runs; kept here so the tests prove the two agree.
    /// </summary>
    public static bool Verify(string secret, string body, string? header, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        string? timestamp = null;
        var candidates = new List<string>();

        foreach (var part in header.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();

            if (key == "t")
            {
                timestamp = value;
            }
            else if (key == Scheme)
            {
                candidates.Add(value);
            }
        }

        if (timestamp is null
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if ((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > tolerance)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(secret, timestamp, body));
        return candidates.Any(c =>
            CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(c)));
    }

    private static string Compute(string secret, string timestamp, string body)
    {
        var mac = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}"));

        return Convert.ToHexStringLower(mac);
    }
}
