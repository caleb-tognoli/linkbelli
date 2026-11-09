using System.Net;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

public class LinkEnricherImageTests
{
    [Fact]
    public async Task Image_link_is_its_own_thumbnail_not_a_failure()
    {
        var (enricher, db, _) = LinkEnricherYouTubeTests.BuildEnricher(HttpStatusCode.OK, "jpeg-bytes", "image/jpeg");
        var link = await AddLink(db, "https://images.ygoprodeck.com/images/cards_cropped/26077387.jpg", title: "Sky Striker Ace - Raye");

        await enricher.EnrichAsync(link.Id);

        Assert.Equal(EnrichmentStatus.Succeeded, link.EnrichmentStatus);
        Assert.Null(link.EnrichmentError);
        Assert.Equal(link.CanonicalUrl, link.ThumbnailUrl);
        Assert.Equal(ContentKind.Image, link.Kind);
        Assert.Equal("Sky Striker Ace - Raye", link.Title); // the source's title is kept
    }

    [Fact]
    public async Task Other_non_html_responses_still_fail()
    {
        var (enricher, db, _) = LinkEnricherYouTubeTests.BuildEnricher(HttpStatusCode.OK, "{}", "application/json");
        var link = await AddLink(db, "https://example.com/data.json");

        await enricher.EnrichAsync(link.Id);

        Assert.Equal(EnrichmentStatus.Failed, link.EnrichmentStatus);
        Assert.Contains("not a web page", link.EnrichmentError);
    }

    private static async Task<Link> AddLink(TestDbContext db, string url, string? title = null)
    {
        var host = new Host { Id = Guid.NewGuid(), Hostname = new Uri(url).Host };
        var link = new Link { Id = Guid.NewGuid(), CanonicalUrl = url, UrlHash = Guid.NewGuid().ToString("N"), HostId = host.Id, Title = title };
        db.Hosts.Add(host);
        db.Links.Add(link);
        await db.SaveChangesAsync();
        return link;
    }
}
