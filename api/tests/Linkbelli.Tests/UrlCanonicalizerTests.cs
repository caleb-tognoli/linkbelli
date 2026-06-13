using Linkbelli.Core.Url;

namespace Linkbelli.Tests;

public class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("https://Example.COM/Article", "https://example.com/Article")] // host lowercased, path preserved
    [InlineData("https://example.com/a#section", "https://example.com/a")]      // fragment dropped
    [InlineData("https://example.com", "https://example.com/")]                 // empty path -> "/"
    [InlineData("https://example.com:443/a", "https://example.com/a")]          // default port dropped
    [InlineData("https://example.com:8080/a", "https://example.com:8080/a")]    // non-default port kept
    [InlineData("https://example.com/a?utm_source=x&id=7", "https://example.com/a?id=7")] // tracking param stripped
    [InlineData("https://example.com/a?b=2&a=1", "https://example.com/a?a=1&b=2")]        // query sorted
    public void Canonicalizes_to_expected_url(string input, string expected)
    {
        Assert.True(UrlCanonicalizer.TryCanonicalize(input, out var result));
        Assert.Equal(expected, result.Url);
    }

    [Fact]
    public void Equivalent_urls_share_one_hash()
    {
        Assert.True(UrlCanonicalizer.TryCanonicalize("https://Example.com/p?utm_medium=e&x=1#top", out var a));
        Assert.True(UrlCanonicalizer.TryCanonicalize("https://example.com/p?x=1", out var b));
        Assert.Equal(b.Hash, a.Hash);
        Assert.Equal("example.com", a.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    public void Rejects_invalid_or_non_http_urls(string input)
    {
        Assert.False(UrlCanonicalizer.TryCanonicalize(input, out _));
    }
}
