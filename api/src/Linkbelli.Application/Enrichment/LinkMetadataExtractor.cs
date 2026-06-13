using AngleSharp.Html.Parser;

namespace Linkbelli.Application.Enrichment;

/// <summary>Metadata pulled from a page: OpenGraph first, then sensible HTML fallbacks.</summary>
public record LinkMetadata(
    string? Title,
    string? Description,
    string? ImageUrl,
    string? SiteName,
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

        var title = Clean(raw.GetValueOrDefault("og:title")) ?? Clean(doc.QuerySelector("title")?.TextContent);
        var description = Clean(raw.GetValueOrDefault("og:description"))
                          ?? Clean(doc.QuerySelector("meta[name='description']")?.GetAttribute("content"));
        var image = Clean(raw.GetValueOrDefault("og:image"));
        var siteName = Clean(raw.GetValueOrDefault("og:site_name"));

        return new LinkMetadata(title, description, image, siteName, raw);
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
