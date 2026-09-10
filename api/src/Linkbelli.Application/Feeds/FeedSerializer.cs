using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace Linkbelli.Application.Feeds;

/// <summary>The syndication formats a playlist can be served as.</summary>
public enum FeedFormat
{
    Rss,
    Atom,
    Json,
}

/// <summary>
/// Turns a <see cref="FeedDocument"/> into RSS 2.0, Atom 1.0 or JSON Feed 1.1. Pure — no I/O — so
/// the output can be asserted on directly. XML is built with LINQ to XML rather than string
/// concatenation, so escaping is the serializer's problem and not ours.
/// </summary>
public static class FeedSerializer
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    public static string ContentType(FeedFormat format) => format switch
    {
        FeedFormat.Rss => "application/rss+xml; charset=utf-8",
        FeedFormat.Atom => "application/atom+xml; charset=utf-8",
        _ => "application/feed+json; charset=utf-8",
    };

    /// <summary>Maps a URL extension (rss/atom/json) to a format; null when unrecognised.</summary>
    public static FeedFormat? Parse(string? extension) => extension?.ToLowerInvariant() switch
    {
        "rss" or "xml" => FeedFormat.Rss,
        "atom" => FeedFormat.Atom,
        "json" => FeedFormat.Json,
        _ => null,
    };

    public static string Serialize(FeedDocument feed, FeedFormat format) => format switch
    {
        FeedFormat.Rss => Rss(feed),
        FeedFormat.Atom => AtomFeed(feed),
        _ => Json(feed),
    };

    private static string Rss(FeedDocument feed)
    {
        var channel = new XElement("channel",
            new XElement("title", feed.Title),
            new XElement("link", feed.HtmlUrl),
            new XElement("description", feed.Description ?? feed.Title),
            new XElement("lastBuildDate", Rfc1123(feed.Updated)),
            new XElement("generator", "Linkbelli"),
            // rel="self" is required for a well-formed feed and is how readers de-duplicate it.
            new XElement(Atom + "link",
                new XAttribute("href", feed.SelfUrl),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")));

        foreach (var entry in feed.Entries)
        {
            var item = new XElement("item",
                new XElement("title", entry.Title),
                new XElement("link", entry.Url),
                // isPermaLink=false: the guid is our item id, not a fetchable address.
                new XElement("guid", new XAttribute("isPermaLink", "false"), entry.Id),
                new XElement("pubDate", Rfc1123(entry.Published)));

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                item.Add(new XElement("description", entry.Summary));
            }

            if (!string.IsNullOrWhiteSpace(entry.Author))
            {
                // RSS's own <author> is specified as an email address; dc:creator is the
                // conventional way to name a person instead.
                item.Add(new XElement(DublinCore + "creator", entry.Author));
            }

            if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
            {
                item.Add(new XElement(Media + "thumbnail", new XAttribute("url", entry.ImageUrl)));
            }

            channel.Add(item);
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "atom", Atom),
                new XAttribute(XNamespace.Xmlns + "media", Media),
                new XAttribute(XNamespace.Xmlns + "dc", DublinCore),
                channel));

        return Render(document);
    }

    private static string AtomFeed(FeedDocument feed)
    {
        var root = new XElement(Atom + "feed",
            new XElement(Atom + "title", feed.Title),
            new XElement(Atom + "id", feed.SelfUrl),
            new XElement(Atom + "updated", Rfc3339(feed.Updated)),
            new XElement(Atom + "generator", "Linkbelli"),
            new XElement(Atom + "author", new XElement(Atom + "name", feed.AuthorName)),
            new XElement(Atom + "link", new XAttribute("rel", "self"), new XAttribute("href", feed.SelfUrl)),
            new XElement(Atom + "link", new XAttribute("rel", "alternate"), new XAttribute("href", feed.HtmlUrl)));

        if (!string.IsNullOrWhiteSpace(feed.Description))
        {
            root.Add(new XElement(Atom + "subtitle", feed.Description));
        }

        foreach (var entry in feed.Entries)
        {
            var element = new XElement(Atom + "entry",
                new XElement(Atom + "title", entry.Title),
                // A urn:uuid id stays stable even if the link's URL is later re-canonicalized.
                new XElement(Atom + "id", $"urn:uuid:{entry.Id}"),
                new XElement(Atom + "updated", Rfc3339(entry.Published)),
                new XElement(Atom + "published", Rfc3339(entry.Published)),
                new XElement(Atom + "link", new XAttribute("rel", "alternate"), new XAttribute("href", entry.Url)));

            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                element.Add(new XElement(Atom + "summary", entry.Summary));
            }

            if (!string.IsNullOrWhiteSpace(entry.Author))
            {
                element.Add(new XElement(Atom + "author", new XElement(Atom + "name", entry.Author)));
            }

            root.Add(element);
        }

        return Render(new XDocument(new XDeclaration("1.0", "utf-8", null), root));
    }

    private static string Json(FeedDocument feed)
    {
        var payload = new Dictionary<string, object?>
        {
            ["version"] = "https://jsonfeed.org/version/1.1",
            ["title"] = feed.Title,
            ["home_page_url"] = feed.HtmlUrl,
            ["feed_url"] = feed.SelfUrl,
            ["description"] = feed.Description,
            ["authors"] = new[] { new Dictionary<string, object?> { ["name"] = feed.AuthorName } },
            ["items"] = feed.Entries.Select(entry =>
            {
                var item = new Dictionary<string, object?>
                {
                    ["id"] = entry.Id,
                    ["url"] = entry.Url,
                    ["title"] = entry.Title,
                    ["date_published"] = Rfc3339(entry.Published),
                };

                if (!string.IsNullOrWhiteSpace(entry.Summary)) item["summary"] = entry.Summary;
                if (!string.IsNullOrWhiteSpace(entry.ImageUrl)) item["image"] = entry.ImageUrl;
                if (!string.IsNullOrWhiteSpace(entry.Author))
                {
                    item["authors"] = new[] { new Dictionary<string, object?> { ["name"] = entry.Author } };
                }

                return item;
            }).ToArray(),
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // Feed titles are full of quotes, ampersands and non-Latin text; escaping them as \uXXXX
        // is valid JSON but unreadable in a feed reader's debug view.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Rfc1123(DateTimeOffset value) => value.UtcDateTime.ToString("r");

    private static string Rfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static string Render(XDocument document)
    {
        var builder = new StringBuilder();
        using var writer = new Utf8StringWriter(builder);
        document.Save(writer);
        return builder.ToString();
    }

    /// <summary>
    /// A StringWriter that reports UTF-8. XDocument.Save writes the writer's encoding into the
    /// XML declaration, and a plain StringWriter reports UTF-16 — so the served bytes (UTF-8)
    /// contradicted the declaration, which strict feed parsers reject.
    /// </summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
