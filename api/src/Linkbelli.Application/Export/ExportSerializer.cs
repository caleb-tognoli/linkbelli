using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;

namespace Linkbelli.Application.Export;

/// <summary>
/// Renders an <see cref="ExportBundle"/> in each supported format. Pure — no I/O — so the output
/// can be asserted on directly.
/// </summary>
public static class ExportSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(ExportBundle bundle, ExportFormat format) => format switch
    {
        ExportFormat.Json => JsonSerializer.Serialize(bundle, JsonOptions),
        ExportFormat.Csv => Csv(bundle),
        ExportFormat.Html => Html(bundle),
        _ => Opml(bundle),
    };

    /// <summary>
    /// One row per link, with the playlist named alongside. The first two columns are `url` and
    /// `note`, which is exactly what the CSV importer reads — so an export round-trips.
    /// </summary>
    private static string Csv(ExportBundle bundle)
    {
        // CRLF explicitly, not AppendLine: RFC 4180 specifies it, and AppendLine would emit
        // whatever the host platform uses — so the same export differed between a Windows dev
        // box and the Linux container, with a CRLF header above LF rows.
        var csv = new StringBuilder();
        csv.Append("url,note,playlist,title,status,score,tags,added").Append(NewLine);

        foreach (var playlist in bundle.Playlists)
        {
            var tags = string.Join(' ', playlist.Tags);
            foreach (var item in playlist.Items)
            {
                csv.Append(Field(item.Url)).Append(',')
                   .Append(Field(item.Note)).Append(',')
                   .Append(Field(playlist.Name)).Append(',')
                   .Append(Field(item.Title)).Append(',')
                   .Append(Field(item.Status)).Append(',')
                   .Append(Field(item.Score?.ToString(CultureInfo.InvariantCulture))).Append(',')
                   .Append(Field(tags)).Append(',')
                   .Append(Field(item.AddedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                   .Append(NewLine);
            }
        }

        return csv.ToString();
    }

    /// <summary>The line ending RFC 4180 specifies, regardless of the host platform.</summary>
    private const string NewLine = "\r\n";

    /// <summary>RFC 4180: quote when the value contains a comma, quote, or newline; double inner quotes.</summary>
    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        return value.AsSpan().IndexOfAny(",\"\n\r") >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    /// <summary>
    /// The Netscape bookmark format every browser imports. Playlists become folders; the file is
    /// deliberately the loose, unclosed-tag shape browsers expect rather than valid XHTML.
    /// </summary>
    private static string Html(ExportBundle bundle)
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
        html.AppendLine("<!-- Exported from Linkbelli. Import this file into any browser. -->");
        html.AppendLine("""<META HTTP-EQUIV="Content-Type" CONTENT="text/html; charset=UTF-8">""");
        html.AppendLine("<TITLE>Bookmarks</TITLE>");
        html.AppendLine("<H1>Bookmarks</H1>");
        html.AppendLine("<DL><p>");

        foreach (var playlist in bundle.Playlists)
        {
            var added = Seconds(playlist.CreatedAt);
            html.AppendLine($"""    <DT><H3 ADD_DATE="{added}">{Escape(playlist.Name)}</H3>""");
            if (!string.IsNullOrWhiteSpace(playlist.Description))
            {
                html.AppendLine($"    <DD>{Escape(playlist.Description)}");
            }
            html.AppendLine("    <DL><p>");

            foreach (var item in playlist.Items)
            {
                html.AppendLine(
                    $"""        <DT><A HREF="{Escape(item.Url)}" ADD_DATE="{Seconds(item.AddedAt)}">{Escape(item.Title ?? item.Url)}</A>""");
                if (!string.IsNullOrWhiteSpace(item.Note))
                {
                    html.AppendLine($"        <DD>{Escape(item.Note)}");
                }
            }

            html.AppendLine("    </DL><p>");
        }

        html.AppendLine("</DL><p>");
        return html.ToString();
    }

    /// <summary>
    /// OPML subscription list. Only RSS sources appear: OPML describes feed subscriptions, and a
    /// CSS scraper or JSON-API source has no feed URL another reader could subscribe to.
    /// </summary>
    private static string Opml(ExportBundle bundle)
    {
        var body = new XElement("body");

        foreach (var source in bundle.Sources)
        {
            if (!string.Equals(source.Type, "Rss", StringComparison.OrdinalIgnoreCase)) continue;
            if (!source.Config.TryGetValue("feedUrl", out var feedUrl) || string.IsNullOrWhiteSpace(feedUrl)) continue;

            body.Add(new XElement("outline",
                new XAttribute("type", "rss"),
                new XAttribute("text", source.Name),
                new XAttribute("title", source.Name),
                new XAttribute("xmlUrl", feedUrl)));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("opml",
                new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", $"{bundle.Username}'s Linkbelli sources"),
                    new XElement("dateCreated", bundle.ExportedAt.UtcDateTime.ToString("r"))),
                body));

        var builder = new StringBuilder();
        using var writer = new Utf8StringWriter(builder);
        document.Save(writer);
        return builder.ToString();
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    private static string Seconds(DateTimeOffset value) =>
        value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Reports UTF-8 so XDocument.Save writes a declaration matching the served bytes.</summary>
    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
