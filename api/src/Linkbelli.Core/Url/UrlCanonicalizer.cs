using System.Security.Cryptography;
using System.Text;

namespace Linkbelli.Core.Url;

/// <summary>The canonical form of a URL plus its derived host and hash (dedup key).</summary>
public readonly record struct CanonicalUrl(string Url, string Host, string Hash);

/// <summary>
/// Normalizes URLs to a single canonical spelling so the same page dedups regardless
/// of casing, fragments, tracking params, or query ordering. Conservative on purpose:
/// it only strips what is near-universally safe; paths are left untouched.
/// </summary>
public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "gclid", "fbclid", "gbraid", "wbraid", "msclkid", "yclid", "igshid",
        "mc_eid", "mc_cid", "mkt_tok", "_ga", "ref_src",
    };

    public static bool TryCanonicalize(string? input, out CanonicalUrl result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.IsNullOrEmpty(uri.IdnHost))
        {
            return false;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.IdnHost.ToLowerInvariant(); // punycode, lowercased
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
        var query = BuildQuery(uri.Query);

        var url = $"{scheme}://{host}{port}{path}{query}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        result = new CanonicalUrl(url, host, hash);
        return true;
    }

    private static string BuildQuery(string rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery) || rawQuery == "?")
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var pair in rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq >= 0 ? pair[..eq] : pair;
            if (!TrackingParams.Contains(Uri.UnescapeDataString(key)))
            {
                kept.Add(pair);
            }
        }

        if (kept.Count == 0)
        {
            return string.Empty;
        }

        kept.Sort(StringComparer.Ordinal); // stable ordering so param order doesn't fork dedup
        return "?" + string.Join('&', kept);
    }
}
