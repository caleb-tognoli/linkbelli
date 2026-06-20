using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

public class RssSourceInterpreterTests
{
    [Fact]
    public void Parses_rss_items_into_discovered_links()
    {
        const string rss = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel>
              <title>Example Feed</title>
              <item><title>First Post</title><link>https://example.com/1</link><author>alice@example.com</author></item>
              <item><title>Second Post</title><link>https://example.com/2</link></item>
            </channel></rss>
            """;

        var links = RssSourceInterpreter.ParseFeed(rss);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/1", links[0].Url);
        Assert.Equal("First Post", links[0].Metadata?["title"]);
        Assert.Equal("alice@example.com", links[0].Metadata?["author"]);
        Assert.Equal("https://example.com/2", links[1].Url);
        Assert.Equal("Second Post", links[1].Metadata?["title"]);
    }

    [Fact]
    public void Parses_atom_feed_links()
    {
        const string atom = """
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Atom Example</title>
              <entry><title>Entry One</title><link href="https://example.com/a"/></entry>
            </feed>
            """;

        var links = RssSourceInterpreter.ParseFeed(atom);

        Assert.Single(links);
        Assert.Equal("https://example.com/a", links[0].Url);
        Assert.Equal("Entry One", links[0].Metadata?["title"]);
    }

    [Fact]
    public void Extracts_media_thumbnail_from_media_rss()
    {
        const string rss = """
            <?xml version="1.0"?>
            <rss version="2.0" xmlns:media="http://search.yahoo.com/mrss/">
              <channel>
                <item>
                  <title>Video Post</title>
                  <link>https://example.com/video</link>
                  <media:content url="https://example.com/video.mp4" medium="video">
                    <media:thumbnail url="https://example.com/thumb.jpg"/>
                  </media:content>
                </item>
              </channel>
            </rss>
            """;

        var links = RssSourceInterpreter.ParseFeed(rss);

        Assert.Single(links);
        Assert.Equal("https://example.com/thumb.jpg", links[0].Metadata?["thumbnail"]);
    }
}
