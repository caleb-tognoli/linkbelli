using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A search worth coming back to. What it matches is whatever matches now — saving the question
/// rather than the answer is the whole point, so these check it keeps up with the collection.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SavedSearchTests(PostgresApiFactory factory)
{
    private record SavedDto(
        Guid Id, string Name, string? Q, string? Host, string[] Tags, string[] ItemTags,
        string? Status, int? MinScore, bool Broken, string? Sort);

    private record HitDto(Guid ItemId, string PlaylistName);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);

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

    private static async Task<SavedDto> SaveAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/v1/search/saved", body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SavedDto>())!;
    }

    private static async Task<SearchPageDto> RunAsync(HttpClient client, Guid id)
    {
        var res = await client.GetAsync($"/api/v1/search/saved/{id}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;
    }

    [Fact]
    public async Task A_search_can_be_saved_and_listed()
    {
        var client = await NewUserAsync();

        var saved = await SaveAsync(client, new
        {
            name = "Unread from one site",
            host = "Seed.Example",
            status = "unwatched",
            minScore = 50,
        });

        Assert.Equal("Unread from one site", saved.Name);
        Assert.Equal("seed.example", saved.Host); // normalized on the way in
        Assert.Equal(50, saved.MinScore);

        var listed = await client.GetFromJsonAsync<List<SavedDto>>("/api/v1/search/saved");
        Assert.Contains(listed!, s => s.Id == saved.Id);
    }

    [Fact]
    public async Task Running_it_returns_what_matches_right_now()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Growing");
        var marker = "topic" + Guid.NewGuid().ToString("N")[..8];

        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => $"{marker} first");

        var saved = await SaveAsync(client, new { name = "That topic", q = marker });
        Assert.Equal(1, (await RunAsync(client, saved.Id)).Total);

        // Something new arrives that matches. A saved list of ids would miss it; a saved
        // question does not.
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => $"{marker} second");

        Assert.Equal(2, (await RunAsync(client, saved.Id)).Total);
    }

    [Fact]
    public async Task Every_filter_survives_being_saved()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Filtered");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        var itemTag = "tag" + Guid.NewGuid().ToString("N")[..8];
        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { tags = new[] { itemTag } }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[0]}/score", new { score = 90 }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[1]}/score", new { score = 10 }))
            .EnsureSuccessStatusCode();

        var saved = await SaveAsync(client, new
        {
            name = "Tagged and highly rated",
            itemTags = new[] { itemTag },
            minScore = 80,
            sort = "score",
        });

        var page = await RunAsync(client, saved.Id);

        var hit = Assert.Single(page.Items);
        Assert.Equal(seeded[0], hit.ItemId);
    }

    [Fact]
    public async Task Deleting_it_leaves_the_links_alone()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Untouched");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var saved = await SaveAsync(client, new { name = "Everything" });
        (await client.DeleteAsync($"/api/v1/search/saved/{saved.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/search/saved/{saved.Id}")).StatusCode);

        // It was only ever a question; the links were never owned by it.
        var items = await client.GetAsync($"/api/v1/playlists/{playlist}/items");
        items.EnsureSuccessStatusCode();
        Assert.Equal(2, (await items.Content.ReadFromJsonAsync<SearchPageDto>())!.Items.Count);
    }

    [Fact]
    public async Task A_saved_search_needs_a_name()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/search/saved", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Saved_searches_do_not_cross_between_accounts()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var saved = await SaveAsync(owner, new { name = "Private question" });

        Assert.DoesNotContain(
            await stranger.GetFromJsonAsync<List<SavedDto>>("/api/v1/search/saved") ?? [],
            s => s.Id == saved.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/search/saved/{saved.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/search/saved/{saved.Id}")).StatusCode);
    }

    [Fact]
    public async Task Running_one_only_ever_sees_its_owners_links()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var marker = "shared" + Guid.NewGuid().ToString("N")[..8];
        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");

        await factory.SeedEnrichedItemsAsync(mine, 1, title: _ => $"{marker} mine");
        await factory.SeedEnrichedItemsAsync(theirs, 1, title: _ => $"{marker} theirs");

        var saved = await SaveAsync(owner, new { name = "Marker", q = marker });

        Assert.Equal(1, (await RunAsync(owner, saved.Id)).Total);
    }
}
