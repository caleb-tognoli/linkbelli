using System.Net.Http;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Pre-run credential login helper. If <c>auth.loginUrl</c> is present in config, POSTs all
/// other <c>auth.*</c> keys as JSON (e.g. username, password, remme) and returns the resulting
/// cookies as a ready-to-use <c>Cookie</c> header value. Returns null when auth is not configured.
/// </summary>
public static class AuthLogin
{
    public const string LoginUrlKey = "auth.loginUrl";
    public const string AuthPrefix = "auth.";

    public static async Task<string?> TryLoginAsync(
        IReadOnlyDictionary<string, string> config,
        HttpClient client,
        CancellationToken ct = default)
    {
        if (!config.TryGetValue(LoginUrlKey, out var loginUrl) || string.IsNullOrEmpty(loginUrl))
            return null;

        var formFields = config
            .Where(kv => kv.Key.StartsWith(AuthPrefix, StringComparison.OrdinalIgnoreCase)
                      && !kv.Key.Equals(LoginUrlKey, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new KeyValuePair<string, string>(kv.Key[AuthPrefix.Length..], kv.Value))
            .ToList();

        using var req = new HttpRequestMessage(HttpMethod.Post, loginUrl)
        {
            Content = new FormUrlEncodedContent(formFields)
        };
        using var resp = await client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var cookies = resp.Headers
            .Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            .SelectMany(h => h.Value)
            .Select(sc => sc.Split(';')[0].Trim())
            .Where(s => s.Contains('='))
            .ToList();

        return cookies.Count > 0 ? string.Join("; ", cookies) : null;
    }

    public static bool IsSecretKey(string key) =>
        key.StartsWith(AuthPrefix, StringComparison.OrdinalIgnoreCase) &&
        !key.Equals(LoginUrlKey, StringComparison.OrdinalIgnoreCase);
}
