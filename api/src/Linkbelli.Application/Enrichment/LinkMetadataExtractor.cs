using AngleSharp.Html.Parser;

namespace Linkbelli.Application.Enrichment;

/// <summary>Metadata pulled from a page: OpenGraph first, then sensible HTML fallbacks.</summary>
public record LinkMetadata(
    string? Title,
    string? Description,
    string? ImageUrl,
    string? SiteName,
    bool Nsfw,
    IReadOnlyDictionary<string, string> Raw);

/// <summary>Pure HTML → metadata extraction (no I/O), so it's unit-testable from fixtures.</summary>
public class LinkMetadataExtractor
{
    private static readonly HtmlParser Parser = new();

    public LinkMetadata Extract(string html)
    {
        var doc = Parser.ParseDocument(html);

        // Collect raw OpenGraph tags for the metadata jsonb bag.
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meta in doc.QuerySelectorAll("meta[property^='og:']"))
        {
            var key = meta.GetAttribute("property");
            var content = meta.GetAttribute("content");
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(content))
            {
                raw[key] = content.Trim();
            }
        }

        var docTitle = Clean(doc.QuerySelector("title")?.TextContent);
        var title = Clean(raw.GetValueOrDefault("og:title")) ?? docTitle;
        var description = Clean(raw.GetValueOrDefault("og:description"))
                          ?? Clean(doc.QuerySelector("meta[name='description']")?.GetAttribute("content"));
        var image = Clean(raw.GetValueOrDefault("og:image"));
        // Fall back to the page <title> when the site doesn't declare og:site_name.
        var siteName = Clean(raw.GetValueOrDefault("og:site_name")) ?? docTitle;

        return new LinkMetadata(title, description, image, siteName, DetectNsfw(doc), raw);
    }

    /// <summary>
    /// Automatic adult-content detection via standard machine-readable rating signals: the
    /// <c>&lt;meta name="rating"&gt;</c> tag (adult/mature/restricted) and the RTA label. These are
    /// the conventional self-declared signals; a richer classifier could be layered on later.
    /// </summary>
    private static bool DetectNsfw(AngleSharp.Dom.IDocument doc)
    {
        foreach (var meta in doc.QuerySelectorAll("meta[name]"))
        {
            if (!string.Equals(meta.GetAttribute("name"), "rating", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = meta.GetAttribute("content")?.Trim().ToLowerInvariant() ?? string.Empty;
            if (content is "adult" or "mature" or "restricted" || content.Contains("rta-5042"))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
