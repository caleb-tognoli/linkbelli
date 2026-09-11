using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Tags used to exist only on playlists, which describes a list rather than the thing in it —
/// so nothing was findable across the lists it happened to sit in. These cover tags on the link.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemTagTests(PostgresApiFactory factory)
{
    private record TaggedItemDto(Guid Id, string? Note, string[] Tags);
    private record HitDto(Guid ItemId, string PlaylistName, string[] Tags);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);
    private record PagedItemsDto(List<TaggedItemDto> Items, string? NextCursor, int? Total);

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

    private static async Task<TaggedItemDto> TagAsync(HttpClient client, Guid itemId, params string[] tags)
    {
        var res = await client.PatchAsJsonAsync($"/api/v1/items/{itemId}", new { tags });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<TaggedItemDto>())!;
    }

    [Fact]
    public async Task A_link_can_be_tagged_and_reports_its_tags()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Tagging");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var item = await TagAsync(client, seeded[0], "Postgres", "performance");

        // Normalized the same way playlist tags are.
        Assert.Equal(["postgres", "performance"], item.Tags.OrderByDescending(t => t).ToArray());
    }

    [Fact]
    public async Task Tags_come_back_on_the_item_list()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Listed tags");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        await TagAsync(client, seeded[0], "reference");

        var page = await client.GetFromJsonAsync<PagedItemsDto>($"/api/v1/playlists/{playlist}/items");

        Assert.Equal(["reference"], page!.Items.Single(i => i.Id == seeded[0]).Tags);
        Assert.Empty(page.Items.Single(i => i.Id == seeded[1]).Tags);
    }

    [Fact]
    public async Task Sending_tags_replaces_the_whole_set()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Replaced tags");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        await TagAsync(client, seeded[0], "one", "two");
        var after = await TagAsync(client, seeded[0], "three");

        // A client sends the tags it wants, so removing one needs no verb of its own.
        Assert.Equal(["three"], after.Tags);
    }

    [Fact]
    public async Task An_empty_set_clears_the_tags()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Cleared tags");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        await TagAsync(client, seeded[0], "temporary");
        var after = await TagAsync(client, seeded[0]);

        Assert.Empty(after.Tags);
    }

    [Fact]
    public async Task An_unrelated_update_leaves_the_tags_alone()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Untouched tags");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        await TagAsync(client, seeded[0], "keep me");

        var res = await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = "a note" });
        res.EnsureSuccessStatusCode();
        var item = (await res.Content.ReadFromJsonAsync<TaggedItemDto>())!;

        Assert.Equal("a note", item.Note);
        Assert.Equal(["keep me"], item.Tags);
    }

    [Fact]
    public async Task A_tag_finds_a_link_across_every_playlist_it_sits_in()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        var inReading = await factory.SeedEnrichedItemsAsync(reading, 2);
        var inWatching = await factory.SeedEnrichedItemsAsync(watching, 1);

        var marker = "topic" + Guid.NewGuid().ToString("N")[..8];
        await TagAsync(client, inReading[0], marker);
        await TagAsync(client, inWatching[0], marker);

        var res = await client.GetAsync($"/api/v1/search?itemTag={marker}");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        // This is the whole point: the same subject in two different lists, found together.
        Assert.Equal(2, page.Total);
        Assert.Contains(page.Items, h => h.PlaylistName == "Reading");
        Assert.Contains(page.Items, h => h.PlaylistName == "Watching");
    }

    [Fact]
    public async Task Several_item_tags_must_all_be_present()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Narrowing");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        var one = "alpha" + Guid.NewGuid().ToString("N")[..6];
        var two = "beta" + Guid.NewGuid().ToString("N")[..6];

        await TagAsync(client, seeded[0], one, two);
        await TagAsync(client, seeded[1], one);

        var res = await client.GetAsync($"/api/v1/search?itemTag={one}&itemTag={two}");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        var hit = Assert.Single(page.Items);
        Assert.Equal(seeded[0], hit.ItemId);
    }

    [Fact]
    public async Task Item_tags_do_not_cross_between_accounts()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");
        var myItems = await factory.SeedEnrichedItemsAsync(mine, 1);
        var theirItems = await factory.SeedEnrichedItemsAsync(theirs, 1);

        var marker = "shared" + Guid.NewGuid().ToString("N")[..8];
        await TagAsync(owner, myItems[0], marker);
        await TagAsync(stranger, theirItems[0], marker);

        var res = await owner.GetAsync($"/api/v1/search?itemTag={marker}");
        res.EnsureSuccessStatusCode();

        // The tag itself is global and deduplicated; what it is attached to is not.
        Assert.Equal(1, (await res.Content.ReadFromJsonAsync<SearchPageDto>())!.Total);
    }
}
