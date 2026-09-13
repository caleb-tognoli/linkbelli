using System.Net;
using System.Text;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Http;
using Linkbelli.Application.Observability;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Microsoft.Extensions.Logging.Abstractions;

namespace Linkbelli.Tests;

/// <summary>
/// Where the fetch actually ended up.
/// </summary>
/// <remarks>
/// Saving <c>/wiki/RSS</c> and <c>/wiki/Really_Simple_Syndication</c> — the second 301s to the
/// first — produced two rows with the same title, and the duplicates page reported nothing saved
/// twice. The client had followed the redirect all along; the enricher threw away the answer.
/// </remarks>
public class LinkRedirectTests
{
    private const string Html = "<html><head><title>RSS - Wikipedia</title></head><body><p>x</p></body></html>";

    private static async Task<Link> EnrichAsync(string requested, string landedAt)
    {
        var handler = new RedirectingHandler(landedAt);
        var db = new TestDbContext();

        var host = new Host { Id = Guid.NewGuid(), Hostname = new Uri(requested).Host };
        UrlCanonicalizer.TryCanonicalize(requested, out var canonical);
        var link = new Link
        {
            Id = Guid.NewGuid(),
            CanonicalUrl = canonical.Url,
            UrlHash = canonical.Hash,
            HostId = host.Id,
        };

        db.Hosts.Add(host);
        db.Links.Add(link);
        await db.SaveChangesAsync();

        var enricher = new LinkEnricher(
            new SingleClientFactory(new HttpClient(handler)),
            new LinkMetadataExtractor(),
            new ArticleExtractor(),
            db,
            new NullThrottle(),
            new AppMetrics(),
            NullLogger<LinkEnricher>.Instance);

        await enricher.EnrichAsync(link.Id);

        return (await db.Links.FindAsync(link.Id))!;
    }

    [Fact]
    public async Task A_fetch_that_ends_somewhere_else_records_where()
    {
        var link = await EnrichAsync(
            "https://en.wikipedia.org/wiki/Really_Simple_Syndication",
            "https://en.wikipedia.org/wiki/RSS");

        Assert.Equal("https://en.wikipedia.org/wiki/RSS", link.ResolvedUrl);
        Assert.NotNull(link.ResolvedUrlHash);
        Assert.NotEqual(link.UrlHash, link.ResolvedUrlHash);

        // The address it was saved under is untouched. That is what the person typed, what the
        // playlist shows, and what they would expect to open.
        Assert.Equal("https://en.wikipedia.org/wiki/Really_Simple_Syndication", link.CanonicalUrl);
    }

    /// <summary>
    /// The common case, and the reason the column is nullable and mostly empty. A row full of
    /// "itself" would make every link a duplicate of itself.
    /// </summary>
    [Fact]
    public async Task A_fetch_that_goes_nowhere_records_nothing()
    {
        var link = await EnrichAsync("https://example.com/article", "https://example.com/article");

        Assert.Null(link.ResolvedUrl);
        Assert.Null(link.ResolvedUrlHash);
    }

    /// <summary>
    /// A redirect that only adds something the canonicalizer strips is not a redirect worth
    /// recording — both addresses already resolve to one row.
    /// </summary>
    [Fact]
    public async Task A_redirect_that_canonicalizes_back_to_the_same_address_records_nothing()
    {
        var link = await EnrichAsync(
            "https://example.com/article",
            "https://example.com/article?utm_source=newsletter");

        Assert.Null(link.ResolvedUrlHash);
    }

    /// <summary>
    /// The shortener, which is the case with no host or path in common at all — so the
    /// host-and-path grouping the duplicates page already had could never have caught it.
    /// </summary>
    [Fact]
    public async Task A_shortener_records_the_page_it_opens()
    {
        var link = await EnrichAsync("https://exmpl.co/abc123", "https://example.com/the-real-article");

        UrlCanonicalizer.TryCanonicalize("https://example.com/the-real-article", out var target);
        Assert.Equal(target.Hash, link.ResolvedUrlHash);
    }

    /// <summary>Answers every request from the address it claims the request ended at.</summary>
    private sealed class RedirectingHandler(string landedAt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Html, Encoding.UTF8, "text/html"),
                // What HttpClient leaves behind after following redirects: the request as it
                // finally went out, pointing at wherever the chain ended.
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, landedAt),
            };

            return Task.FromResult(response);
        }
    }
}
