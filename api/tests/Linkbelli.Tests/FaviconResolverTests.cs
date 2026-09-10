using Linkbelli.Core.Url;

namespace Linkbelli.Tests;

public class FaviconResolverTests
{
    [Theory]
    [InlineData("https://example.com/blog/post", "/assets/icon.png", "https://example.com/assets/icon.png")]
    [InlineData("https://example.com/blog/post", "icon.png", "https://example.com/blog/icon.png")]
    [InlineData("https://example.com/blog/post", "../icon.png", "https://example.com/icon.png")]
    [InlineData("https://example.com/blog/post", "//cdn.example/i.png", "https://cdn.example/i.png")]
    [InlineData("https://example.com/blog/post", "https://cdn.example/i.png", "https://cdn.example/i.png")]
    public void Resolves_relative_hrefs_against_the_page(string page, string href, string expected)
    {
        Assert.Equal(expected, FaviconResolver.Resolve(page, href));
    }

    [Fact]
    public void Falls_back_to_the_site_root_when_no_icon_is_declared()
    {
        Assert.Equal("https://example.com/favicon.ico",
            FaviconResolver.Resolve("https://example.com/deep/page?x=1", null));
    }

    [Fact]
    public void Blank_href_is_treated_as_no_icon()
    {
        Assert.Equal("https://example.com/favicon.ico",
            FaviconResolver.Resolve("https://example.com/a", "   "));
    }

    [Fact]
    public void Keeps_a_small_data_uri_verbatim()
    {
        const string dataUri = "data:image/png;base64,iVBORw0KGgo=";

        Assert.Equal(dataUri, FaviconResolver.Resolve("https://example.com/a", dataUri));
    }

    [Fact]
    public void Drops_an_oversized_data_uri_rather_than_storing_it_on_the_host()
    {
        var huge = "data:image/png;base64," + new string('A', FaviconResolver.MaxDataUriLength);

        Assert.Null(FaviconResolver.Resolve("https://example.com/a", huge));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:someone@example.com")]
    public void Rejects_non_http_schemes(string href)
    {
        Assert.Null(FaviconResolver.Resolve("https://example.com/a", href));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/x")]
    [InlineData("")]
    public void Rejects_an_unusable_page_url(string page)
    {
        Assert.Null(FaviconResolver.Resolve(page, "/icon.png"));
    }
}
