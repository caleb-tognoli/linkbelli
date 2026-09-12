using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Putting a source's history into a playlist that was attached to it later.
/// </summary>
/// <remarks>
/// This read SourceRun.ItemsAdded and called it "all distinct URLs ever discovered by this
/// source". It is not, in three separate ways: the array holds at most SourceRun.SampleSize URLs
/// per run, it holds only the ones that were new to the whole application when the run found them,
/// and SourceRunRetention deletes old runs. So a source that had been running for a month offered
/// to restore a handful of links out of thousands, and reported that number to the UI as well.
///
/// It now reads the items the source created, which is what it actually produced and is not
/// sampled, filtered or pruned.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class SourceBackfillTests(PostgresApiFactory factory)
{
    private record BackfillResult(int ItemsAdded);

    private record SubscribeResult(int DiscoveredCount);

    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"P {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private async Task<Guid> NewSourceAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"S {Guid.NewGuid():N}",
            type = "Rss",
            schedule = "0 3 * * *",
            config = new { feedUrl = $"https://feed{Guid.NewGuid():N}.example/rss" },
            playlistIds = new[] { playlistId },
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SourceDto>())!.Id;
    }

    /// <summary>
    /// Marks a playlist's items as having come from a source, which is what a real run does and
    /// what the backfill now reads.
    /// </summary>
    private async Task AttributeItemsToSourceAsync(Guid playlistId, Guid sourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.SourceId, sourceId));
    }

    /// <summary>
    /// More than a run keeps for inspection, which is the whole point: the old implementation
    /// silently stopped at SourceRun.SampleSize.
    /// </summary>
    private const int MoreThanASample = SourceRun.SampleSize + 15;

    [Fact]
    public async Task Brings_across_more_than_a_run_keeps_for_inspection()
    {
        var client = await SignedInAsync();

        var origin = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(origin, MoreThanASample);
        var sourceId = await NewSourceAsync(client, origin);
        await AttributeItemsToSourceAsync(origin, sourceId);

        var target = await NewPlaylistAsync(client);
        await client.PostAsJsonAsync($"/api/v1/playlists/{target}/sources", new { sourceId });

        var res = await client.PostAsync($"/api/v1/playlists/{target}/sources/{sourceId}/backfill", null);
        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<BackfillResult>();

        Assert.Equal(MoreThanASample, result!.ItemsAdded);
    }

    [Fact]
    public async Task Says_up_front_how_much_there_is_to_bring_across()
    {
        var client = await SignedInAsync();

        var origin = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(origin, MoreThanASample);
        var sourceId = await NewSourceAsync(client, origin);
        await AttributeItemsToSourceAsync(origin, sourceId);

        var target = await NewPlaylistAsync(client);

        // The number the subscribe call reports is what the UI offers, so it has to be the same
        // number the backfill will actually deliver.
        var res = await client.PostAsJsonAsync($"/api/v1/playlists/{target}/sources", new { sourceId });
        res.EnsureSuccessStatusCode();
        var available = await res.Content.ReadFromJsonAsync<SubscribeResult>();

        Assert.Equal(MoreThanASample, available!.DiscoveredCount);
    }

    [Fact]
    public async Task Adds_nothing_twice()
    {
        var client = await SignedInAsync();

        var origin = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(origin, 5);
        var sourceId = await NewSourceAsync(client, origin);
        await AttributeItemsToSourceAsync(origin, sourceId);

        var target = await NewPlaylistAsync(client);
        await client.PostAsJsonAsync($"/api/v1/playlists/{target}/sources", new { sourceId });

        var first = await client.PostAsync($"/api/v1/playlists/{target}/sources/{sourceId}/backfill", null);
        var second = await client.PostAsync($"/api/v1/playlists/{target}/sources/{sourceId}/backfill", null);

        Assert.Equal(5, (await first.Content.ReadFromJsonAsync<BackfillResult>())!.ItemsAdded);
        Assert.Equal(0, (await second.Content.ReadFromJsonAsync<BackfillResult>())!.ItemsAdded);
    }

    [Fact]
    public async Task Leaves_links_the_source_did_not_find_alone()
    {
        var client = await SignedInAsync();

        var origin = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(origin, 4);
        var sourceId = await NewSourceAsync(client, origin);
        await AttributeItemsToSourceAsync(origin, sourceId);

        // A second playlist whose items nothing attributed to this source.
        var unrelated = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(unrelated, 6);

        var target = await NewPlaylistAsync(client);
        await client.PostAsJsonAsync($"/api/v1/playlists/{target}/sources", new { sourceId });

        var res = await client.PostAsync($"/api/v1/playlists/{target}/sources/{sourceId}/backfill", null);

        Assert.Equal(4, (await res.Content.ReadFromJsonAsync<BackfillResult>())!.ItemsAdded);
    }

    [Fact]
    public async Task A_source_that_has_produced_nothing_backfills_nothing()
    {
        var client = await SignedInAsync();

        var origin = await NewPlaylistAsync(client);
        var sourceId = await NewSourceAsync(client, origin);

        var target = await NewPlaylistAsync(client);
        await client.PostAsJsonAsync($"/api/v1/playlists/{target}/sources", new { sourceId });

        var res = await client.PostAsync($"/api/v1/playlists/{target}/sources/{sourceId}/backfill", null);

        Assert.Equal(0, (await res.Content.ReadFromJsonAsync<BackfillResult>())!.ItemsAdded);
    }
}
