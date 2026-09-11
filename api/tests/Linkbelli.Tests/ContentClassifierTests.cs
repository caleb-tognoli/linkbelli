using Linkbelli.Core.Content;

namespace Linkbelli.Tests;

public class ContentClassifierTests
{
    private static ContentKind Classify(
        string url, string? ogType = null, string? mediaType = null, int? wordCount = null) =>
        ContentClassifier.Classify(url, new Uri(url).Host, ogType, mediaType, wordCount);

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc", ContentKind.Video)]
    [InlineData("https://youtu.be/abc", ContentKind.Video)]
    [InlineData("https://m.youtube.com/watch?v=abc", ContentKind.Video)]
    [InlineData("https://github.com/dotnet/runtime", ContentKind.Repository)]
    [InlineData("https://arxiv.org/abs/2501.00001", ContentKind.Paper)]
    [InlineData("https://doi.org/10.1000/xyz", ContentKind.Paper)]
    [InlineData("https://news.ycombinator.com/item?id=1", ContentKind.Social)]
    [InlineData("https://soundcloud.com/artist/track", ContentKind.Audio)]
    [InlineData("https://imgur.com/a/xyz", ContentKind.Image)]
    public void The_host_says_what_it_is(string url, ContentKind expected)
    {
        Assert.Equal(expected, Classify(url));
    }

    [Fact]
    public void A_host_that_merely_ends_in_a_known_name_is_not_that_host()
    {
        Assert.Equal(ContentKind.Unknown, Classify("https://notyoutube.com/watch"));
        Assert.Equal(ContentKind.Video, Classify("https://music.youtube.com/watch"));
    }

    [Fact]
    public void What_was_served_beats_what_the_address_suggests()
    {
        // A "page" that turns out to be a PDF is a document, whatever it is called.
        Assert.Equal(ContentKind.Document, Classify("https://example.com/report", mediaType: "application/pdf"));
        Assert.Equal(ContentKind.Video, Classify("https://example.com/thing", mediaType: "video/mp4"));
    }

    [Fact]
    public void A_pdf_is_recognised_by_its_path_when_nothing_was_fetched()
    {
        Assert.Equal(ContentKind.Document, Classify("https://example.com/papers/thesis.pdf"));
    }

    [Fact]
    public void A_query_string_mentioning_pdf_does_not_make_it_one()
    {
        // The path is what names the file; the query is often a tracking parameter.
        Assert.Equal(ContentKind.Unknown, Classify("https://example.com/page?download=thesis.pdf"));
    }

    [Fact]
    public void A_page_that_declares_itself_is_believed()
    {
        Assert.Equal(ContentKind.Article, Classify("https://example.com/post", ogType: "article"));
        Assert.Equal(ContentKind.Video, Classify("https://example.com/clip", ogType: "video.other"));
        Assert.Equal(ContentKind.Audio, Classify("https://example.com/song", ogType: "music.song"));
    }

    [Fact]
    public void A_video_page_with_a_long_description_is_still_a_video()
    {
        // The host is checked before the prose, or every well-described video becomes an article.
        Assert.Equal(
            ContentKind.Video,
            Classify("https://www.youtube.com/watch?v=abc", ogType: "article", wordCount: 5_000));
    }

    [Fact]
    public void Enough_prose_makes_it_an_article_when_nothing_else_said_so()
    {
        Assert.Equal(ContentKind.Article, Classify("https://example.com/x", wordCount: ContentClassifier.ArticleWords));
        Assert.Equal(ContentKind.Unknown, Classify("https://example.com/x", wordCount: 40));
    }

    [Fact]
    public void Nothing_known_stays_unknown_rather_than_guessing()
    {
        // Unknown is a real answer: a landing page is not an article, and calling it one would
        // put it in front of someone looking for something to read.
        Assert.Equal(ContentKind.Unknown, Classify("https://example.com/"));
    }

    [Fact]
    public void A_url_that_will_not_parse_does_not_throw()
    {
        Assert.Equal(ContentKind.Unknown, ContentClassifier.Classify("not a url", "", null, null, null));
    }
}
