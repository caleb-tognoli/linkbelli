using System.Text.Json;
using System.Xml.Linq;
using Linkbelli.Application.Export;

namespace Linkbelli.Tests;

public class ExportSerializerTests
{
    private static ExportItem Item(
        string url = "https://example.com/a",
        string? title = "A post",
        string? note = null,
        int? score = null) =>
        new(Guid.Parse("0195f2c1-0000-7000-8000-000000000001"), url, title, "The description", note,
            "Added", score, null, "Example", new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero), null);

    private static ExportBundle Bundle(params ExportItem[] items) => new(
        "alice",
        new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        [new ExportFolder(Guid.NewGuid(), "Reading", null)],
        [
            new ExportPlaylist(Guid.NewGuid(), "Weekend reading", "weekend-reading", "Second looks",
                "Public", ["tech", "ai"], null, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), items),
        ],
        [
            new ExportSource(Guid.NewGuid(), "BBC News", "Rss", "0 * * * *", "Active", "Private",
                new Dictionary<string, string> { ["feedUrl"] = "https://feeds.bbci.co.uk/news/rss.xml" }, []),
            new ExportSource(Guid.NewGuid(), "A scraper", "Scraper", "0 * * * *", "Active", "Private",
                new Dictionary<string, string> { ["url"] = "https://news.example", ["header.Authorization"] = "***" }, []),
        ]);

    [Theory]
    [InlineData(null, ExportFormat.Json)]
    [InlineData("", ExportFormat.Json)]
    [InlineData("json", ExportFormat.Json)]
    [InlineData("CSV", ExportFormat.Csv)]
    [InlineData("html", ExportFormat.Html)]
    [InlineData("bookmarks", ExportFormat.Html)]
    [InlineData("opml", ExportFormat.Opml)]
    public void Parses_the_requested_format(string? requested, ExportFormat expected)
    {
        Assert.Equal(expected, ExportFormats.Parse(requested));
    }

    [Fact]
    public void Rejects_an_unknown_format()
    {
        Assert.Null(ExportFormats.Parse("pdf"));
    }

    [Fact]
    public void Json_keeps_the_whole_structure()
    {
        using var doc = JsonDocument.Parse(ExportSerializer.Serialize(Bundle(Item()), ExportFormat.Json));
        var root = doc.RootElement;

        Assert.Equal("alice", root.GetProperty("username").GetString());
        var playlist = root.GetProperty("playlists")[0];
        Assert.Equal("Weekend reading", playlist.GetProperty("name").GetString());
        Assert.Equal("Public", playlist.GetProperty("visibility").GetString());
        Assert.Equal(2, playlist.GetProperty("tags").GetArrayLength());
        Assert.Equal("https://example.com/a", playlist.GetProperty("items")[0].GetProperty("url").GetString());
        Assert.Equal("Reading", root.GetProperty("folders")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Csv_leads_with_url_and_note_so_it_round_trips_through_the_importer()
    {
        var csv = ExportSerializer.Serialize(Bundle(Item(note: "read later")), ExportFormat.Csv);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("url,note,playlist,title,status,score,tags,added", lines[0]);
        Assert.StartsWith("https://example.com/a,read later,Weekend reading,A post,Added,,tech ai,", lines[1]);
    }

    [Fact]
    public void Csv_quotes_fields_that_would_otherwise_break_the_row()
    {
        var awkward = Item(title: """He said "hi", then left""", note: "line one\nline two");

        var csv = ExportSerializer.Serialize(Bundle(awkward), ExportFormat.Csv);

        // The note's newline is inside quotes, so the record legitimately spans two lines.
        Assert.Contains("\"line one\nline two\"", csv);
        Assert.Contains("\"He said \"\"hi\"\", then left\"", csv);
    }

    [Fact]
    public void Csv_of_an_empty_export_is_just_the_header()
    {
        var empty = new ExportBundle("alice", DateTimeOffset.UtcNow, [], [], []);

        Assert.Equal("url,note,playlist,title,status,score,tags,added\r\n",
            ExportSerializer.Serialize(empty, ExportFormat.Csv));
    }

    [Fact]
    public void Html_is_a_netscape_bookmark_file_with_a_folder_per_playlist()
    {
        var html = ExportSerializer.Serialize(Bundle(Item()), ExportFormat.Html);

        Assert.StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file-1>", html);
        Assert.Contains("<H3 ADD_DATE=", html);
        Assert.Contains("Weekend reading</H3>", html);
        Assert.Contains("""<A HREF="https://example.com/a" ADD_DATE="1772366400">A post</A>""", html);
    }

    [Fact]
    public void Html_escapes_a_title_that_contains_markup()
    {
        var hostile = Item(title: """<script>alert("x")</script> & more""");

        var html = ExportSerializer.Serialize(Bundle(hostile), ExportFormat.Html);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Opml_lists_feed_sources_only()
    {
        var opml = XDocument.Parse(ExportSerializer.Serialize(Bundle(Item()), ExportFormat.Opml));
        var outlines = opml.Root!.Element("body")!.Elements("outline").ToList();

        // The scraper source has no feed URL another reader could subscribe to, so it is left out.
        var outline = Assert.Single(outlines);
        Assert.Equal("BBC News", outline.Attribute("text")!.Value);
        Assert.Equal("https://feeds.bbci.co.uk/news/rss.xml", outline.Attribute("xmlUrl")!.Value);
        Assert.Equal("rss", outline.Attribute("type")!.Value);
    }

    [Fact]
    public void Opml_declares_utf_8_to_match_the_bytes_served()
    {
        Assert.StartsWith("""<?xml version="1.0" encoding="utf-8"?>""",
            ExportSerializer.Serialize(Bundle(Item()), ExportFormat.Opml));
    }

    [Theory]
    [InlineData(ExportFormat.Json, "json")]
    [InlineData(ExportFormat.Csv, "csv")]
    [InlineData(ExportFormat.Html, "html")]
    [InlineData(ExportFormat.Opml, "opml")]
    public void Each_format_names_its_own_file_extension(ExportFormat format, string expected)
    {
        Assert.Equal(expected, ExportFormats.FileExtension(format));
    }
}
