using Linkbelli.Application.Common;
using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

public class ScraperSourceInterpreterTests
{
    private const string Html = """
        <html><body>
          <ul class="stories">
            <li><a class="headline" href="/articles/1">First Story</a></li>
            <li><a class="headline" href="https://other.example/2">Second Story</a></li>
            <li><a class="headline" href="/articles/1">First Story (dupe)</a></li>
            <li><a class="headline">No href, skipped</a></li>
          </ul>
        </body></html>
        """;

    [Fact]
    public void Extracts_links_and_resolves_relative_urls()
    {
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = "a.headline",
        };

        var links = ScraperSourceInterpreter.Parse(Html, "https://news.example/section", config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://news.example/articles/1", links[0].Url);
        Assert.Equal("https://other.example/2", links[1].Url);
    }

    [Fact]
    public void Honors_custom_link_attribute_and_meta_selectors()
    {
        const string html = """
            <div class="card" data-url="https://example.com/a"><h2 class="t">Title A</h2><span class="author">Alice</span></div>
            <div class="card" data-url="https://example.com/b"><h2 class="t">Title B</h2><span class="author">Bob</span></div>
            """;
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = ".card",
            [ScraperSourceInterpreter.LinkAttributeKey] = "data-url",
            ["meta.title"] = "h2.t",
            ["meta.author"] = ".author",
        };

        var links = ScraperSourceInterpreter.Parse(html, "https://example.com", config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/a", links[0].Url);
        Assert.Equal("Title A", links[0].Metadata?["title"]);
        Assert.Equal("Alice", links[0].Metadata?["author"]);
    }

    [Fact]
    public void Meta_regex_strips_matches_and_expands_capture_groups()
    {
        const string html = """
            <div class="card" data-url="https://example.com/a"><h2 class="t">Story A | Site Name</h2><span class="by">Posted by Alice</span></div>
            <div class="card" data-url="https://example.com/b"><h2 class="t">Story B</h2><span class="by">Posted by Bob</span></div>
            """;
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = ".card",
            [ScraperSourceInterpreter.LinkAttributeKey] = "data-url",
            ["meta.title"] = "h2.t",
            ["meta.title.regex"] = @"\s*\|\s*Site Name$",
            ["meta.author"] = ".by",
            ["meta.author.regex"] = "^Posted by (.+)$",
            ["meta.author.replacement"] = "$1",
        };

        var links = ScraperSourceInterpreter.Parse(html, "https://example.com", config);

        Assert.Equal(2, links.Count);
        Assert.Equal("Story A", links[0].Metadata?["title"]);
        Assert.Equal("Alice", links[0].Metadata?["author"]);
        // Non-matching values pass through untouched.
        Assert.Equal("Story B", links[1].Metadata?["title"]);
        Assert.Equal("Bob", links[1].Metadata?["author"]);
    }

    [Fact]
    public void Meta_regex_that_empties_the_value_drops_the_field()
    {
        const string html = """<div class="card" data-url="https://example.com/a"><h2 class="t">Site Name</h2></div>""";
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = ".card",
            [ScraperSourceInterpreter.LinkAttributeKey] = "data-url",
            ["meta.title"] = "h2.t",
            ["meta.title.regex"] = "^Site Name$",
        };

        var links = ScraperSourceInterpreter.Parse(html, "https://example.com", config);

        var link = Assert.Single(links);
        Assert.Null(link.Metadata);
    }

    [Fact]
    public void Unparseable_meta_regex_is_rejected_on_validate()
    {
        // ValidateConfig never touches the http factory.
        var interpreter = new ScraperSourceInterpreter(null!);
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.UrlKey] = "https://example.com",
            [ScraperSourceInterpreter.ItemSelectorKey] = ".card",
            ["meta.title"] = "h2.t",
            ["meta.title.regex"] = "([unclosed",
        };

        var ex = Assert.Throws<ValidationException>(() => interpreter.ValidateConfig(config));
        Assert.Contains("config.meta.title.regex", ex.Errors.Keys);
    }

    [Fact]
    public void Link_selector_finds_child_element()
    {
        const string html = """
            <html><body>
              <div class="item"><a class="link" href="/watch/1">Watch</a><img src="thumb1.jpg" /></div>
              <div class="item"><a class="link" href="/watch/2">Watch</a><img src="thumb2.jpg" /></div>
            </body></html>
            """;
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = ".item",
            [ScraperSourceInterpreter.LinkSelectorKey] = "a.link",
            [ScraperSourceInterpreter.LinkAttributeKey] = "href",
            ["meta.thumbnail"] = "img",
            ["meta.thumbnail.attr"] = "src",
        };

        var links = ScraperSourceInterpreter.Parse(html, "https://example.com", config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/watch/1", links[0].Url);
        Assert.Equal("thumb1.jpg", links[0].Metadata?["thumbnail"]);
    }
}
