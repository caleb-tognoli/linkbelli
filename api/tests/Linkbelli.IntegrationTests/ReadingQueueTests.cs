using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// "What should I read next", across every playlist. The ordering is the feature: a queue that
/// always surfaces the newest arrival is how a backlog becomes permanent.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ReadingQueueTests(PostgresApiFactory factory)
{
    private record HitDto(Guid ItemId, string PlaylistName, int? Score, string Status);
    private record QueueDto(List<HitDto> Items, string? NextCursor, int? Total);

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

    private static async Task<QueueDto> QueueAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<QueueDto>("/api/v1/search?status=unwatched&sort=queue&limit=50"))!;

    /// <summary>Spreads creation times apart so "longest carried" has something to order by.</summary>
    private async Task AgeAsync(Guid playlistId, Func<int, TimeSpan> age)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var items = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .OrderBy(i => i.Position).ToListAsync();

        for (var n = 0; n < items.Count; n++)
        {
            items[n].CreationTime = DateTimeOffset.UtcNow - age(n);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rated_things_come_first_highest_first()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Queue");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[0]}/score", new { score = 40 }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[1]}/score", new { score = 90 }))
            .EnsureSuccessStatusCode();

        var queue = await QueueAsync(client);

        Assert.Equal([90, 40, null], queue.Items.Select(h => h.Score).ToArray());
    }

    [Fact]
    public async Task Among_the_unrated_the_oldest_comes_first()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Backlog");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        // Item 0 is the oldest; a queue that led with the newest would bury it forever.
        await AgeAsync(playlist, n => TimeSpan.FromDays(30 - n));

        var queue = await QueueAsync(client);

        Assert.Equal(seeded[0], queue.Items[0].ItemId);
    }

    [Fact]
    public async Task Finished_things_are_not_in_the_queue()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Partly done");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        (await client.PostAsJsonAsync("/api/v1/items/bulk",
            new { itemIds = seeded.Take(2), action = "SetStatus", status = "Watched" }))
            .EnsureSuccessStatusCode();

        var queue = await QueueAsync(client);

        Assert.Single(queue.Items);
        Assert.All(queue.Items, h => Assert.Equal("Added", h.Status));
    }

    [Fact]
    public async Task The_queue_spans_every_playlist()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        await factory.SeedEnrichedItemsAsync(reading, 1);
        await factory.SeedEnrichedItemsAsync(watching, 1);

        var queue = await QueueAsync(client);

        Assert.Equal(2, queue.Total);
        Assert.Contains(queue.Items, h => h.PlaylistName == "Reading");
        Assert.Contains(queue.Items, h => h.PlaylistName == "Watching");
    }

    [Fact]
    public async Task An_empty_queue_is_empty_rather_than_everything()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "All done");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        (await client.PostAsJsonAsync("/api/v1/items/bulk",
            new { itemIds = seeded, action = "SetStatus", status = "Watched" }))
            .EnsureSuccessStatusCode();

        Assert.Empty((await QueueAsync(client)).Items);
    }

    [Fact]
    public async Task The_queue_never_reaches_into_another_account()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");
        await factory.SeedEnrichedItemsAsync(mine, 1);
        await factory.SeedEnrichedItemsAsync(theirs, 3);

        Assert.Equal(1, (await QueueAsync(owner)).Total);
    }
}
