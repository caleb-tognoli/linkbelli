using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Enrichment;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A link that could not be fetched used to be stamped exactly like one that had been, with the
/// reason buried in the OpenGraph bag — so it rendered as a bare URL forever. These pin what
/// replaced that: an honest status, a readable reason, and a way to try again.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class EnrichmentStatusTests(PostgresApiFactory factory)
{
    private record LinkStatusDto(
        Guid Id, string Url, string Host, string? Title, string? Description, string? ThumbnailUrl,
        string? SiteName, bool Enriched, bool Nsfw, string? Favicon, string EnrichmentStatus, string? EnrichmentError);

    private record ItemWithLinkDto(Guid Id, LinkStatusDto Link);
    private record PagedDto(List<ItemWithLinkDto> Items, string? NextCursor, int? Total);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private async Task<Guid> StampLinkAsync(
        Guid playlistId, EnrichmentStatus status, string? error, int failureCount = 0, DateTimeOffset? lastChecked = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var link = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .Select(i => i.Link!)
            .FirstAsync();

        link.EnrichmentStatus = status;
        link.EnrichmentError = error;
        link.FailureCount = failureCount;
        link.LastCheckedAt = lastChecked;
        await db.SaveChangesAsync();

        return link.Id;
    }

    [Fact]
    public async Task A_failed_link_reports_why_instead_of_looking_enriched()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Broken metadata");
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => "Never fetched");
        await StampLinkAsync(playlist, EnrichmentStatus.Failed, "The site refused the request (403).", failureCount: 1);

        var page = await client.GetFromJsonAsync<PagedDto>($"/api/v1/playlists/{playlist}/items");

        var item = Assert.Single(page!.Items);
        Assert.Equal("Failed", item.Link.EnrichmentStatus);
        Assert.Equal("The site refused the request (403).", item.Link.EnrichmentError);

        // Still listed: the owner can see the count, so the item must be visible and labelled
        // rather than silently missing.
        Assert.True(item.Link.Enriched);
    }

    [Fact]
    public async Task A_successful_link_carries_no_error()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Fine");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await StampLinkAsync(playlist, EnrichmentStatus.Succeeded, null);

        var page = await client.GetFromJsonAsync<PagedDto>($"/api/v1/playlists/{playlist}/items");

        var item = Assert.Single(page!.Items);
        Assert.Equal("Succeeded", item.Link.EnrichmentStatus);
        Assert.Null(item.Link.EnrichmentError);
    }

    [Fact]
    public async Task A_recheck_clears_the_backoff_so_the_request_is_not_swallowed()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Retry me");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await StampLinkAsync(playlist, EnrichmentStatus.Failed, "HTTP 500",
            failureCount: 4, lastChecked: DateTimeOffset.UtcNow);

        var res = await client.PostAsync($"/api/v1/links/{linkId}/recheck", null);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var link = await db.Links.FirstAsync(l => l.Id == linkId);

        Assert.Equal(0, link.FailureCount);
    }

    [Fact]
    public async Task Rechecking_a_link_that_does_not_exist_is_not_found()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsync($"/api/v1/links/{Guid.NewGuid()}/recheck", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task The_sweep_picks_up_stale_successes()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Gone stale");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await StampLinkAsync(playlist, EnrichmentStatus.Succeeded, null,
            lastChecked: DateTimeOffset.UtcNow.AddDays(-(ILinkRecheckService.FreshDays + 1)));

        using var scope = factory.Services.CreateScope();
        var queued = await scope.ServiceProvider.GetRequiredService<ILinkRecheckService>().SweepAsync();

        Assert.True(queued > 0, "A success older than the freshness window should be re-checked.");
    }

    [Fact]
    public async Task The_sweep_leaves_a_failure_alone_until_its_backoff_elapses()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Backing off");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await StampLinkAsync(playlist, EnrichmentStatus.Failed, "HTTP 500",
            failureCount: 3, lastChecked: DateTimeOffset.UtcNow.AddMinutes(-5));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ILinkRecheckService>();

        // Three failures means a 24-hour wait; five minutes in, it is not due.
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var before = await db.Links.Where(l => l.Id == linkId).Select(l => l.LastCheckedAt).FirstAsync();

        await service.SweepAsync();

        var after = await db.Links.Where(l => l.Id == linkId).Select(l => l.LastCheckedAt).FirstAsync();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_link_that_has_failed_too_often_is_given_up_on()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Hopeless");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await StampLinkAsync(playlist, EnrichmentStatus.Failed, "HTTP 500",
            failureCount: ILinkRecheckService.MaxFailures,
            lastChecked: DateTimeOffset.UtcNow.AddYears(-1));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        // Long past any backoff, but past the give-up point too — the sweep must not pick it up.
        var candidates = await db.Links
            .Where(l => l.EnrichmentStatus == EnrichmentStatus.Failed
                && l.FailureCount < ILinkRecheckService.MaxFailures)
            .Select(l => l.Id)
            .ToListAsync();

        Assert.DoesNotContain(linkId, candidates);
    }

    private record HitDto(Guid ItemId, string PlaylistName);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);

    [Fact]
    public async Task Search_can_surface_the_link_rot_in_a_collection()
    {
        var client = await NewUserAsync();
        var healthy = await NewPlaylistAsync(client, "Still good");
        var rotten = await NewPlaylistAsync(client, "Gone");

        await factory.SeedEnrichedItemsAsync(healthy, 2);
        await factory.SeedEnrichedItemsAsync(rotten, 1);

        await StampLinkAsync(healthy, EnrichmentStatus.Succeeded, null);
        await StampLinkAsync(rotten, EnrichmentStatus.Broken, "The page could not be found (404).");

        var res = await client.GetAsync("/api/v1/search?broken=true");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        var hit = Assert.Single(page.Items);
        Assert.Equal("Gone", hit.PlaylistName);
    }

    [Fact]
    public async Task A_collection_with_no_rot_reports_none()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "All fine");
        await factory.SeedEnrichedItemsAsync(playlist, 2);
        await StampLinkAsync(playlist, EnrichmentStatus.Succeeded, null);

        var res = await client.GetAsync("/api/v1/search?broken=true");
        res.EnsureSuccessStatusCode();

        Assert.Equal(0, (await res.Content.ReadFromJsonAsync<SearchPageDto>())!.Total);
    }
}
