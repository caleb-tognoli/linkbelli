namespace Linkbelli.Core.Url;

/// <summary>
/// Turns a page's declared icon href into an absolute URL. Pure URL arithmetic, kept out of the
/// enricher so it can be exercised without a fetch.
/// </summary>
public static class FaviconResolver
{
    /// <summary>A data: icon longer than this is dropped rather than stored inline on the host row.</summary>
    public const int MaxDataUriLength = 2048;

    /// <summary>
    /// Resolves <paramref name="href"/> against the page it was declared on. A page that declares
    /// no icon falls back to /favicon.ico at the site root — the convention every browser assumes.
    /// Returns null when the page URL is unusable or the href resolves to a non-http(s) scheme.
    /// </summary>
    public static string? Resolve(string pageUrl, string? href)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)
            || page.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return new Uri(page, "/favicon.ico").ToString();
        }

        href = href.Trim();

        // Data URIs are already self-contained — keep small ones, drop anything that would
        // bloat the host row.
        if (href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return href.Length <= MaxDataUriLength ? href : null;
        }

        return Uri.TryCreate(page, href, out var resolved) && resolved.Scheme is "http" or "https"
            ? resolved.ToString()
            : null;
    }
}
