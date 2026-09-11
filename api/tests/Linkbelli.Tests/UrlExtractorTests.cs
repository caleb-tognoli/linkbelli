using Linkbelli.Core.Url;

namespace Linkbelli.Tests;

public class UrlExtractorTests
{
    [Fact]
    public void Nothing_in_means_nothing_out()
    {
        Assert.Empty(UrlExtractor.Extract(null));
        Assert.Empty(UrlExtractor.Extract("   "));
        Assert.Empty(UrlExtractor.Extract("no addresses here at all"));
    }

    [Fact]
    public void Addresses_are_found_in_ordinary_prose()
    {
        var found = UrlExtractor.Extract(
            "Have a look at https://example.com/one and then https://example.com/two when you can.");

        Assert.Equal(["https://example.com/one", "https://example.com/two"], found);
    }

    [Fact]
    public void One_per_line_is_the_common_case()
    {
        var found = UrlExtractor.Extract("""
            https://example.com/a
            https://example.com/b
            https://example.com/c
            """);

        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void The_order_they_were_pasted_in_is_kept()
    {
        // A pasted list is usually in a deliberate order; shuffling it makes the result look like
        // something else happened.
        var found = UrlExtractor.Extract("https://example.com/z https://example.com/a");

        Assert.Equal(["https://example.com/z", "https://example.com/a"], found);
    }

    [Fact]
    public void The_same_address_twice_is_one_address()
    {
        var found = UrlExtractor.Extract("https://example.com/x and again https://example.com/x");

        Assert.Single(found);
    }

    [Theory]
    [InlineData("Read https://example.com/post.", "https://example.com/post")]
    [InlineData("Read https://example.com/post, then go.", "https://example.com/post")]
    [InlineData("(see https://example.com/post)", "https://example.com/post")]
    [InlineData("\"https://example.com/post\"", "https://example.com/post")]
    [InlineData("<https://example.com/post>", "https://example.com/post")]
    public void Punctuation_from_the_prose_around_it_is_trimmed(string text, string expected)
    {
        Assert.Equal([expected], UrlExtractor.Extract(text));
    }

    [Fact]
    public void Punctuation_that_belongs_to_the_address_is_kept()
    {
        // A closing bracket is only noise if nothing inside the address opened it — Wikipedia
        // alone would lose thousands of links to the simpler rule.
        var found = UrlExtractor.Extract("https://en.wikipedia.org/wiki/Rust_(programming_language)");

        Assert.Equal(["https://en.wikipedia.org/wiki/Rust_(programming_language)"], found);
    }

    [Fact]
    public void A_markdown_link_gives_up_its_address()
    {
        var found = UrlExtractor.Extract("[the post](https://example.com/post) is worth reading");

        Assert.Equal(["https://example.com/post"], found);
    }

    [Fact]
    public void Query_strings_and_fragments_survive()
    {
        var found = UrlExtractor.Extract("https://example.com/search?q=rust&page=2#results");

        Assert.Equal(["https://example.com/search?q=rust&page=2#results"], found);
    }

    [Fact]
    public void Plain_http_counts()
    {
        Assert.Single(UrlExtractor.Extract("http://old.example.com/page"));
    }

    [Fact]
    public void Something_that_is_not_a_web_address_is_left_alone()
    {
        // Nothing here can be fetched, and pretending otherwise would fill a playlist with rows
        // that can only ever fail.
        Assert.Empty(UrlExtractor.Extract("ftp://example.com/file mailto:someone@example.com"));
    }

    [Fact]
    public void A_paste_is_capped()
    {
        var text = string.Join("\n", Enumerable.Range(0, UrlExtractor.MaxUrls + 50)
            .Select(n => $"https://example.com/{n}"));

        Assert.Equal(UrlExtractor.MaxUrls, UrlExtractor.Extract(text).Count);
    }
}
