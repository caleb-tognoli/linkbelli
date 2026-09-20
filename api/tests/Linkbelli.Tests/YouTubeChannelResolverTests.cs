using System.Net;
using System.Text;
using Linkbelli.Application.Common;
using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

/// <summary>
/// Turning what somebody has of a YouTube channel into the id its feed needs.
/// </summary>
/// <remarks>
/// The template used to ask for the id and say to find it in the page source or paste a handle
/// into a converter — an instruction to go and use another website, in the simplest template the
/// product ships.
/// </remarks>
public class YouTubeChannelResolverTests
{
    private const string Id = "UCBa659QWEk1AI4Tg--mrJ2A";

    [Theory]
    [InlineData(Id)]
    [InlineData("  " + Id + "  ")]
    [InlineData("https://www.youtube.com/channel/" + Id)]
    [InlineData("https://www.youtube.com/channel/" + Id + "/videos")]
    public async Task Takes_an_id_or_a_channel_address_without_asking_youtube(string input)
    {
        var (resolver, handler) = Build(HttpStatusCode.OK, "");

        Assert.Equal(Id, await resolver.ResolveAsync(input));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("@veritasium")]
    [InlineData("veritasium")]
    [InlineData("https://www.youtube.com/@veritasium")]
    [InlineData("youtube.com/@veritasium/videos")]
    [InlineData("https://www.youtube.com/c/veritasium")]
    public async Task Looks_a_handle_up_on_the_channels_own_page(string input)
    {
        var (resolver, handler) = Build(
            HttpStatusCode.OK,
            $$"""<html><head><link rel="canonical" href="https://www.youtube.com/channel/{{Id}}"></head></html>""");

        Assert.Equal(Id, await resolver.ResolveAsync(input));

        // Always youtube.com, whatever was typed: the URL is built from the handle, not from the
        // text somebody pasted.
        var asked = Assert.Single(handler.Requests);
        Assert.Equal("www.youtube.com", asked.RequestUri!.Host);
        Assert.Equal("/@veritasium", asked.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task Says_so_when_there_is_no_such_channel()
    {
        var (resolver, _) = Build(HttpStatusCode.NotFound, "");

        var error = await Assert.ThrowsAsync<ValidationException>(() => resolver.ResolveAsync("@nobodyhere"));
        Assert.Contains("no channel", error.Errors["channelId"][0], StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/@someone")]
    [InlineData("not a channel at all!")]
    public async Task Refuses_what_is_none_of_those(string input)
    {
        var (resolver, handler) = Build(HttpStatusCode.OK, "");

        await Assert.ThrowsAsync<ValidationException>(() => resolver.ResolveAsync(input));
        Assert.Empty(handler.Requests);
    }

    private static (YouTubeChannelResolver Resolver, PageHandler Handler) Build(HttpStatusCode status, string body)
    {
        var handler = new PageHandler(status, body);
        var resolver = new YouTubeChannelResolver(new SingleClientFactory(new HttpClient(handler)));
        return (resolver, handler);
    }

    private sealed class PageHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/html"),
            });
        }
    }
}
