using System.Text.Json;
using System.Xml.Linq;
using Linkbelli.Application.Feeds;

namespace Linkbelli.Tests;

public class FeedSerializerTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    private static FeedDocument Sample(params FeedEntry[] entries) => new(
        "Weekend Reading",
        "Things worth a second look",
        "https://linkbelli.test/api/v1/public/playlists/alice/weekend-reading/feed.rss",
        "https://linkbelli.test/public/alice/weekend-reading",
        "alice",
        new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        entries);

    private static FeedEntry Entry(string title = "A post", string? summary = "Why it matters") => new(
        "0195f2c1-0000-7000-8000-000000000001",
        title,
        "https://example.com/post",
        summary,
        new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
        "https://cdn.example.com/cover.png",
        "Jane Roe");

    [Theory]
    [InlineData("rss", FeedFormat.Rss)]
    [InlineData("RSS", FeedFormat.Rss)]
    [InlineData("xml", FeedFormat.Rss)]
    [InlineData("atom", FeedFormat.Atom)]
    [InlineData("json", FeedFormat.Json)]
    public void Parses_the_url_extension(string extension, FeedFormat expected)
    {
        Assert.Equal(expected, FeedSerializer.Parse(extension));
    }

    [Theory]
    [InlineData("html")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_an_unknown_extension(string? extension)
    {
        Assert.Null(FeedSerializer.Parse(extension));
    }

    [Fact]
    public void Rss_carries_the_channel_and_a_self_link()
    {
        var xml = XDocument.Parse(FeedSerializer.Serialize(Sample(Entry()), FeedFormat.Rss));
        var channel = xml.Root!.Element("channel")!;

        Assert.Equal("2.0", xml.Root.Attribute("version")!.Value);
        Assert.Equal("Weekend Reading", channel.Element("title")!.Value);
        Assert.Equal("Things worth a second look", channel.Element("description")!.Value);
        Assert.Equal("https://linkbelli.test/public/alice/weekend-reading", channel.Element("link")!.Value);

        var self = channel.Elements(Atom + "link").Single(l => l.Attribute("rel")!.Value == "self");
        Assert.EndsWith("/feed.rss", self.Attribute("href")!.Value);
    }

    [Fact]
    public void Rss_items_point_at_the_link_not_at_linkbelli()
    {
        var xml = XDocument.Parse(FeedSerializer.Serialize(Sample(Entry()), FeedFormat.Rss));
        var item = xml.Root!.Element("channel")!.Element("item")!;

        Assert.Equal("A post", item.Element("title")!.Value);
        Assert.Equal("https://example.com/post", item.Element("link")!.Value);
        Assert.Equal("Why it matters", item.Element("description")!.Value);
        Assert.Equal("Jane Roe", item.Element(DublinCore + "creator")!.Value);
        Assert.Equal("https://cdn.example.com/cover.png", item.Element(Media + "thumbnail")!.Attribute("url")!.Value);

        // The guid is our item id, which is not a fetchable address.
        var guid = item.Element("guid")!;
        Assert.Equal("false", guid.Attribute("isPermaLink")!.Value);
        Assert.Equal("0195f2c1-0000-7000-8000-000000000001", guid.Value);

        Assert.Equal("Sun, 01 Mar 2026 12:00:00 GMT", item.Element("pubDate")!.Value);
    }

    [Fact]
    public void Atom_uses_urn_uuid_ids_and_rfc3339_timestamps()
    {
        var xml = XDocument.Parse(FeedSerializer.Serialize(Sample(Entry()), FeedFormat.Atom));
        var entry = xml.Root!.Element(Atom + "entry")!;

        Assert.Equal("2026-03-04T05:06:07Z", xml.Root.Element(Atom + "updated")!.Value);
        Assert.Equal("alice", xml.Root.Element(Atom + "author")!.Element(Atom + "name")!.Value);
        Assert.Equal("urn:uuid:0195f2c1-0000-7000-8000-000000000001", entry.Element(Atom + "id")!.Value);
        Assert.Equal("2026-03-01T12:00:00Z", entry.Element(Atom + "published")!.Value);
        Assert.Equal(
            "https://example.com/post",
            entry.Element(Atom + "link")!.Attribute("href")!.Value);
    }

    [Fact]
    public void Json_feed_declares_version_1_1_and_maps_the_entry()
    {
        using var doc = JsonDocument.Parse(FeedSerializer.Serialize(Sample(Entry()), FeedFormat.Json));
        var root = doc.RootElement;

        Assert.Equal("https://jsonfeed.org/version/1.1", root.GetProperty("version").GetString());
        Assert.Equal("Weekend Reading", root.GetProperty("title").GetString());
        Assert.Equal("alice", root.GetProperty("authors")[0].GetProperty("name").GetString());

        var item = root.GetProperty("items")[0];
        Assert.Equal("https://example.com/post", item.GetProperty("url").GetString());
        Assert.Equal("A post", item.GetProperty("title").GetString());
        Assert.Equal("2026-03-01T12:00:00Z", item.GetProperty("date_published").GetString());
        Assert.Equal("https://cdn.example.com/cover.png", item.GetProperty("image").GetString());
        Assert.Equal("Jane Roe", item.GetProperty("authors")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Optional_fields_are_omitted_rather_than_emitted_empty()
    {
        var bare = new FeedEntry("id-1", "Bare", "https://example.com/x", null,
            DateTimeOffset.UnixEpoch, null, null);

        var rss = XDocument.Parse(FeedSerializer.Serialize(Sample(bare), FeedFormat.Rss));
        var item = rss.Root!.Element("channel")!.Element("item")!;
        Assert.Null(item.Element("description"));
        Assert.Null(item.Element(Media + "thumbnail"));
        Assert.Null(item.Element(DublinCore + "creator"));

        using var json = JsonDocument.Parse(FeedSerializer.Serialize(Sample(bare), FeedFormat.Json));
        var jsonItem = json.RootElement.GetProperty("items")[0];
        Assert.False(jsonItem.TryGetProperty("summary", out _));
        Assert.False(jsonItem.TryGetProperty("image", out _));
        Assert.False(jsonItem.TryGetProperty("authors", out _));
    }

    [Fact]
    public void Markup_in_a_title_is_escaped_not_injected()
    {
        var hostile = Entry(title: """Tom & Jerry's <script>alert("x")</script>""", summary: "a < b");

        var raw = FeedSerializer.Serialize(Sample(hostile), FeedFormat.Rss);
        Assert.DoesNotContain("<script>", raw);

        // Still parses, and round-trips back to the original text.
        var item = XDocument.Parse(raw).Root!.Element("channel")!.Element("item")!;
        Assert.Equal("""Tom & Jerry's <script>alert("x")</script>""", item.Element("title")!.Value);
        Assert.Equal("a < b", item.Element("description")!.Value);
    }

    [Fact]
    public void An_empty_playlist_still_produces_a_valid_feed()
    {
        var rss = XDocument.Parse(FeedSerializer.Serialize(Sample(), FeedFormat.Rss));
        Assert.Empty(rss.Root!.Element("channel")!.Elements("item"));
        Assert.Equal("Wed, 04 Mar 2026 05:06:07 GMT", rss.Root.Element("channel")!.Element("lastBuildDate")!.Value);

        var atom = XDocument.Parse(FeedSerializer.Serialize(Sample(), FeedFormat.Atom));
        Assert.Empty(atom.Root!.Elements(Atom + "entry"));

        using var json = JsonDocument.Parse(FeedSerializer.Serialize(Sample(), FeedFormat.Json));
        Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void A_playlist_without_a_description_still_has_an_rss_description()
    {
        var noDescription = Sample(Entry()) with { Description = null };

        var channel = XDocument.Parse(FeedSerializer.Serialize(noDescription, FeedFormat.Rss))
            .Root!.Element("channel")!;

        // RSS requires <description>; falling back to the title beats emitting an empty element.
        Assert.Equal("Weekend Reading", channel.Element("description")!.Value);
    }

    [Theory]
    [InlineData(FeedFormat.Rss, "application/rss+xml; charset=utf-8")]
    [InlineData(FeedFormat.Atom, "application/atom+xml; charset=utf-8")]
    [InlineData(FeedFormat.Json, "application/feed+json; charset=utf-8")]
    public void Each_format_declares_its_own_content_type(FeedFormat format, string expected)
    {
        Assert.Equal(expected, FeedSerializer.ContentType(format));
    }

    [Theory]
    [InlineData(FeedFormat.Rss)]
    [InlineData(FeedFormat.Atom)]
    public void The_xml_declaration_says_utf_8(FeedFormat format)
    {
        // The bytes are served as UTF-8. A StringWriter reports UTF-16, and XDocument.Save writes
        // whatever the writer reports — so this once claimed an encoding the response did not use.
        var raw = FeedSerializer.Serialize(Sample(Entry()), format);

        Assert.StartsWith("""<?xml version="1.0" encoding="utf-8"?>""", raw);
    }
}
