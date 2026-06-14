using Linkbelli.Application.Enrichment;

namespace Linkbelli.Tests;

public class LinkMetadataExtractorTests
{
    private readonly LinkMetadataExtractor _extractor = new();

    [Fact]
    public void Prefers_opengraph_tags()
    {
        const string html = """
            <html><head>
              <title>Fallback Title</title>
              <meta name="description" content="fallback desc">
              <meta property="og:title" content="OG Title">
              <meta property="og:description" content="OG description">
              <meta property="og:image" content="https://cdn.example.com/x.png">
              <meta property="og:site_name" content="Example">
            </head><body></body></html>
            """;

        var meta = _extractor.Extract(html);

        Assert.Equal("OG Title", meta.Title);
        Assert.Equal("OG description", meta.Description);
        Assert.Equal("https://cdn.example.com/x.png", meta.ImageUrl);
        Assert.Equal("Example", meta.SiteName);
        Assert.Equal("OG Title", meta.Raw["og:title"]);
    }

    [Fact]
    public void Falls_back_to_title_and_meta_description()
    {
        const string html = """
            <html><head>
              <title>  Just a Title  </title>
              <meta name="description" content="plain description">
            </head><body></body></html>
            """;

        var meta = _extractor.Extract(html);

        Assert.Equal("Just a Title", meta.Title); // trimmed
        Assert.Equal("plain description", meta.Description);
        Assert.Null(meta.ImageUrl);
        Assert.Equal("Just a Title", meta.SiteName); // falls back to <title> when no og:site_name
    }

    [Fact]
    public void Returns_nulls_when_nothing_present()
    {
        var meta = _extractor.Extract("<html><body><p>hi</p></body></html>");

        Assert.Null(meta.Title);
        Assert.Null(meta.Description);
        Assert.Empty(meta.Raw);
        Assert.False(meta.Nsfw);
    }

    [Theory]
    [InlineData("adult", true)]
    [InlineData("Mature", true)]
    [InlineData("RTA-5042-1996-1400-1577-RTA", true)]
    [InlineData("general", false)]
    [InlineData("", false)]
    public void Detects_nsfw_from_rating_meta(string rating, bool expected)
    {
        var html = $"<html><head><meta name=\"rating\" content=\"{rating}\"><title>t</title></head><body></body></html>";

        Assert.Equal(expected, _extractor.Extract(html).Nsfw);
    }
}
