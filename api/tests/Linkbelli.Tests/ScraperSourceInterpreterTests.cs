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
        Assert.Equal("First Story", links[0].Title);
        Assert.Equal("https://other.example/2", links[1].Url);
    }

    [Fact]
    public void Honors_custom_link_attribute_and_title_selector()
    {
        const string html = """
            <div class="card" data-url="https://example.com/a"><h2 class="t">Title A</h2></div>
            <div class="card" data-url="https://example.com/b"><h2 class="t">Title B</h2></div>
            """;
        var config = new Dictionary<string, string>
        {
            [ScraperSourceInterpreter.ItemSelectorKey] = ".card",
            [ScraperSourceInterpreter.LinkAttributeKey] = "data-url",
            [ScraperSourceInterpreter.TitleSelectorKey] = "h2.t",
        };

        var links = ScraperSourceInterpreter.Parse(html, "https://example.com", config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/a", links[0].Url);
        Assert.Equal("Title A", links[0].Title);
    }
}
