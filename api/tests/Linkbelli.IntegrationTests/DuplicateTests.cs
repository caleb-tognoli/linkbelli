using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Dedup stops the identical link landing twice in one playlist, and canonicalization strips the
/// tracking parameters it knows about. Neither helps with the same page across three lists, or
/// reached by an address nobody taught the canonicalizer about.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DuplicateTests(PostgresApiFactory factory)
{
    private record CopyDto(Guid ItemId, Guid PlaylistId, string PlaylistName, string Url, string? Title);
    private record GroupDto(string Kind, string Key, List<CopyDto> Copies);

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

    private static async Task<List<GroupDto>> DuplicatesAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/v1/duplicates");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<List<GroupDto>>())!;
    }

    [Fact]
    public async Task The_same_link_in_two_playlists_is_reported()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        var url = $"https://dupe.example/{Guid.NewGuid():N}/article";
        await factory.SeedEnrichedItemsAsync(reading, 1, url: _ => url);
        await factory.SeedEnrichedItemsAsync(watching, 1, url: _ => url);

        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key == url);

        Assert.Equal("SameLink", group.Kind);
        Assert.Equal(2, group.Copies.Count);
        Assert.Contains(group.Copies, c => c.PlaylistName == "Reading");
        Assert.Contains(group.Copies, c => c.PlaylistName == "Watching");
    }

    [Fact]
    public async Task The_same_page_under_two_addresses_is_reported()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Same page");

        var tag = Guid.NewGuid().ToString("N")[..8];
        // Canonicalization strips the tracking parameters it knows; these are two distinct links
        // that nonetheless point at one page.
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://pages.example/{tag}/post?ref=newsletter");
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://pages.example/{tag}/post?variant=b");

        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key.Contains(tag));

        Assert.Equal("SamePage", group.Kind);
        Assert.Equal(2, group.Copies.Count);
    }

    [Fact]
    public async Task A_trailing_slash_is_not_a_different_page()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Slashes");

        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://slash.example/{tag}/post?a=1");
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://slash.example/{tag}/post/?b=2");

        Assert.Single(await DuplicatesAsync(client), g => g.Key.Contains(tag));
    }

    [Fact]
    public async Task A_link_saved_once_is_not_a_duplicate_of_anything()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Unique");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        Assert.Empty(await DuplicatesAsync(client));
    }

    [Fact]
    public async Task Different_pages_on_one_site_are_not_duplicates()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Same site");

        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://site.example/{tag}/one");
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://site.example/{tag}/two");

        Assert.Empty(await DuplicatesAsync(client));
    }

    [Fact]
    public async Task A_group_is_reported_once_not_twice()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "First");
        var watching = await NewPlaylistAsync(client, "Second");

        var url = $"https://once.example/{Guid.NewGuid():N}/article";
        await factory.SeedEnrichedItemsAsync(reading, 1, url: _ => url);
        await factory.SeedEnrichedItemsAsync(watching, 1, url: _ => url);

        // The identical link also shares a host and path with itself; it must not appear again
        // as a near-duplicate of the group it is already in.
        var groups = (await DuplicatesAsync(client))
            .Where(g => g.Copies.Any(c => c.Url == url))
            .ToList();

        Assert.Single(groups);
    }

    [Fact]
    public async Task Duplicates_never_reach_across_accounts()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");

        // The same link saved by two different people is not either person's duplicate.
        var url = $"https://shared.example/{Guid.NewGuid():N}/article";
        await factory.SeedEnrichedItemsAsync(mine, 1, url: _ => url);
        await factory.SeedEnrichedItemsAsync(theirs, 1, url: _ => url);

        Assert.Empty(await DuplicatesAsync(owner));
        Assert.Empty(await DuplicatesAsync(stranger));
    }

    [Fact]
    public async Task Clearing_a_duplicate_with_a_bulk_delete_resolves_the_group()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Keep");
        var watching = await NewPlaylistAsync(client, "Drop");

        var url = $"https://resolve.example/{Guid.NewGuid():N}/article";
        await factory.SeedEnrichedItemsAsync(reading, 1, url: _ => url);
        await factory.SeedEnrichedItemsAsync(watching, 1, url: _ => url);

        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key == url);
        var drop = group.Copies.Single(c => c.PlaylistName == "Drop");

        (await client.PostAsJsonAsync("/api/v1/items/bulk", new { itemIds = new[] { drop.ItemId }, action = "Delete" }))
            .EnsureSuccessStatusCode();

        Assert.DoesNotContain(await DuplicatesAsync(client), g => g.Key == url);
    }
}
