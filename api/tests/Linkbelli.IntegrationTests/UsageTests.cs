using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The size and shape of a collection. Quotas were only ever visible as a 429, and nothing
/// reported the collection itself — so a hundred links could rot in it unnoticed.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class UsageTests(PostgresApiFactory factory)
{
    private record UsageDto(
        int Playlists, int Items, int PendingItems, int Folders, int Sources,
        int SavedSearches, int Sites, int Watched, int Broken, int InTrash);

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

    private static async Task<UsageDto> UsageAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UsageDto>("/api/v1/me/usage"))!;

    [Fact]
    public async Task A_fresh_account_reports_nothing()
    {
        var client = await NewUserAsync();

        var usage = await UsageAsync(client);

        Assert.Equal(0, usage.Playlists);
        Assert.Equal(0, usage.Items);
        Assert.Equal(0, usage.Sites);
        Assert.Equal(0, usage.InTrash);
    }

    [Fact]
    public async Task It_counts_what_is_actually_there()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        await factory.SeedEnrichedItemsAsync(reading, 3);
        await factory.SeedEnrichedItemsAsync(watching, 2);

        var usage = await UsageAsync(client);

        Assert.Equal(2, usage.Playlists);
        Assert.Equal(5, usage.Items);
    }

    [Fact]
    public async Task Distinct_sites_are_counted_not_links()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Sites");
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Three links, two sites.
        await factory.SeedEnrichedItemsAsync(playlist, 2, url: n => $"https://one-{tag}.example/{n}");
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://two-{tag}.example/a");

        Assert.Equal(2, (await UsageAsync(client)).Sites);
    }

    [Fact]
    public async Task Progress_is_reported_alongside_volume()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Progress");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        (await client.PostAsJsonAsync("/api/v1/items/bulk",
            new { itemIds = seeded.Take(3), action = "SetStatus", status = "Watched" }))
            .EnsureSuccessStatusCode();

        var usage = await UsageAsync(client);

        Assert.Equal(4, usage.Items);
        Assert.Equal(3, usage.Watched);
    }

    [Fact]
    public async Task Link_rot_is_surfaced_rather_than_left_to_be_noticed()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Rotting");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var link = await db.PlaylistItems.Where(i => i.PlaylistId == playlist)
                .Select(i => i.Link!).FirstAsync();
            link.EnrichmentStatus = EnrichmentStatus.Broken;
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, (await UsageAsync(client)).Broken);
    }

    [Fact]
    public async Task What_is_in_the_trash_is_counted_because_it_is_otherwise_invisible()
    {
        var client = await NewUserAsync();
        var kept = await NewPlaylistAsync(client, "Kept");
        var dropped = await NewPlaylistAsync(client, "Dropped");
        var seeded = await factory.SeedEnrichedItemsAsync(kept, 3);

        (await client.DeleteAsync($"/api/v1/playlists/{dropped}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();

        var usage = await UsageAsync(client);

        // One playlist and one item, still restorable and otherwise hidden by the query filter.
        Assert.Equal(2, usage.InTrash);
        Assert.Equal(1, usage.Playlists);
        Assert.Equal(2, usage.Items);
    }

    [Fact]
    public async Task Sources_folders_and_saved_searches_are_counted()
    {
        var client = await NewUserAsync();

        (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Reading" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/search/saved", new { name = "Unread" })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "A feed",
            type = "Rss",
            config = new { feedUrl = "https://usage.example/feed.xml" },
            schedule = "0 * * * *",
        })).EnsureSuccessStatusCode();

        var usage = await UsageAsync(client);

        Assert.Equal(1, usage.Folders);
        Assert.Equal(1, usage.Sources);
        Assert.Equal(1, usage.SavedSearches);
    }

    [Fact]
    public async Task Usage_never_counts_another_account()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");
        await factory.SeedEnrichedItemsAsync(mine, 2);
        await factory.SeedEnrichedItemsAsync(theirs, 5);

        var usage = await UsageAsync(owner);

        Assert.Equal(1, usage.Playlists);
        Assert.Equal(2, usage.Items);
    }
}
