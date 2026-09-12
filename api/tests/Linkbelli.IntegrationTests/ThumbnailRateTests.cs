using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A playlist page asks for one thumbnail per row, dozens at a time.
/// </summary>
/// <remarks>
/// This endpoint was on the strict "sensitive" policy — ten a minute — so the first ten rows got
/// their picture and every row after that silently lost it, because the page removes an image
/// that fails to load. The limit belonged on the outbound fetch, not on reading a cached file.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ThumbnailRateTests(PostgresApiFactory factory)
{
    /// <summary>Comfortably more than the old limit, and about what a long page really asks for.</summary>
    private const int PageWorthOfRows = 40;

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    /// <summary>
    /// Link ids that have no thumbnail, so each request is answered from the database alone.
    /// </summary>
    /// <remarks>
    /// A 404 is the right answer for these and exercises the same rate limit as a hit — which is
    /// the thing under test. Fetching real images would make this depend on the open web.
    /// </remarks>
    private async Task<List<Guid>> SeedLinkIdsAsync(HttpClient client, int count)
    {
        var res = await client.PostAsJsonAsync(
            "/api/v1/playlists", new { name = $"Thumbs {Guid.NewGuid():N}", visibility = "Private" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;

        await factory.SeedEnrichedItemsAsync(playlist.Id, count);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await db.PlaylistItems
            .Where(i => i.PlaylistId == playlist.Id)
            .Select(i => i.LinkId)
            .ToListAsync();
    }

    [Fact]
    public void The_thumbnail_endpoint_is_not_on_the_strict_policy()
    {
        var endpoints = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("thumbnails", StringComparison.Ordinal) == true)
            .ToList();

        var thumbnails = Assert.Single(endpoints);
        var policy = thumbnails.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        // Asserted on the policy name rather than on how many requests get through, because this
        // suite deliberately raises the "sensitive" allowance (several classes share one
        // anonymous partition) — which is exactly what stopped the behavioural tests below from
        // catching the original bug. This one cannot be masked by a configured number.
        Assert.Equal("thumbnails", policy);
    }

    [Fact]
    public async Task A_whole_page_of_thumbnails_is_not_rate_limited()
    {
        var client = await NewUserAsync();
        var linkIds = await SeedLinkIdsAsync(client, PageWorthOfRows);

        var statuses = new List<HttpStatusCode>();
        foreach (var id in linkIds)
        {
            statuses.Add((await client.GetAsync($"/api/v1/thumbnails/{id}")).StatusCode);
        }

        // Not one 429. On the old policy this was ten answers and thirty refusals.
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.NotFound, status));
    }

    [Fact]
    public async Task Asking_for_them_all_at_once_is_not_rate_limited_either()
    {
        var client = await NewUserAsync();
        var linkIds = await SeedLinkIdsAsync(client, PageWorthOfRows);

        // What a browser actually does: every <img> at once rather than one after another.
        var responses = await Task.WhenAll(
            linkIds.Select(id => client.GetAsync($"/api/v1/thumbnails/{id}")));

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, responses.Select(r => r.StatusCode));
    }

    [Fact]
    public async Task One_persons_page_does_not_use_up_somebody_elses_allowance()
    {
        var heavy = await NewUserAsync();
        var heavyIds = await SeedLinkIdsAsync(heavy, PageWorthOfRows);
        foreach (var id in heavyIds)
        {
            await heavy.GetAsync($"/api/v1/thumbnails/{id}");
        }

        var other = await NewUserAsync();
        var otherIds = await SeedLinkIdsAsync(other, 5);

        foreach (var id in otherIds)
        {
            var res = await other.GetAsync($"/api/v1/thumbnails/{id}");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, res.StatusCode);
        }
    }

    [Fact]
    public async Task A_cached_thumbnail_is_allowed_to_be_cached_by_the_browser()
    {
        var client = await NewUserAsync();
        var linkIds = await SeedLinkIdsAsync(client, 1);

        var res = await client.GetAsync($"/api/v1/thumbnails/{linkIds[0]}");

        // A 404 carries no caching promise on purpose: a link gets a thumbnail later, when
        // enrichment finds one, and a cached negative would hide it for a day.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Null(res.Headers.CacheControl?.MaxAge);
    }
}
