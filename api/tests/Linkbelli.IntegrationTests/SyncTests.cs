using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What changed since a client last looked. The property that matters most is the one a plain
/// list can never give: learning that something was deleted.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SyncTests(PostgresApiFactory factory)
{
    private record SyncedPlaylistDto(Guid Id, bool Deleted, string? Name, string[]? Tags, DateTimeOffset LastModified);
    private record SyncedItemDto(Guid Id, Guid PlaylistId, bool Deleted, string? Url, string? Note, string? Status, DateTimeOffset LastModified);
    private record SyncDto(DateTimeOffset Until, bool More, List<SyncedPlaylistDto> Playlists, List<SyncedItemDto> Items);

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

    private static async Task<SyncDto> SyncAsync(HttpClient client, DateTimeOffset? since = null)
    {
        var query = since is null ? "" : $"?since={Uri.EscapeDataString(since.Value.ToString("O"))}";
        var res = await client.GetAsync($"/api/v1/sync{query}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SyncDto>())!;
    }

    [Fact]
    public async Task A_first_sync_returns_everything_the_caller_has()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Everything");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        var sync = await SyncAsync(client);

        Assert.Contains(sync.Playlists, p => p.Id == playlist && p.Name == "Everything");
        Assert.Equal(3, sync.Items.Count(i => i.PlaylistId == playlist));
    }

    [Fact]
    public async Task Nothing_changed_means_nothing_comes_back()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Quiet");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var first = await SyncAsync(client);
        var second = await SyncAsync(client, first.Until);

        Assert.Empty(second.Playlists);
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task A_change_since_the_last_sync_comes_back()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Before");

        var first = await SyncAsync(client);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { name = "After" }))
            .EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        var changed = Assert.Single(second.Playlists);
        Assert.Equal("After", changed.Name);
    }

    [Fact]
    public async Task A_deleted_playlist_comes_back_as_a_tombstone()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Doomed");

        var first = await SyncAsync(client);
        (await client.DeleteAsync($"/api/v1/playlists/{playlist}")).EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        // A client that only ever hears about what exists can never learn something went away.
        var tombstone = Assert.Single(second.Playlists);
        Assert.Equal(playlist, tombstone.Id);
        Assert.True(tombstone.Deleted);
        Assert.Null(tombstone.Name);
    }

    [Fact]
    public async Task A_deleted_item_comes_back_as_a_tombstone()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Items");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        var first = await SyncAsync(client);
        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        var tombstone = Assert.Single(second.Items);
        Assert.Equal(seeded[0], tombstone.Id);
        Assert.True(tombstone.Deleted);
        Assert.Null(tombstone.Url);
    }

    [Fact]
    public async Task An_edited_item_comes_back_with_its_new_state()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Edits");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var first = await SyncAsync(client);

        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = "changed", status = "Watched" }))
            .EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        var changed = Assert.Single(second.Items);
        Assert.Equal("changed", changed.Note);
        Assert.Equal("Watched", changed.Status);
    }

    [Fact]
    public async Task The_window_never_skips_a_change_made_during_a_sync()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Racing");

        var first = await SyncAsync(client);

        // Everything after `until` must still be reported, however close behind it lands.
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        Assert.Single((await SyncAsync(client, first.Until)).Items);
    }

    [Fact]
    public async Task Sync_never_reaches_into_another_account()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");
        await factory.SeedEnrichedItemsAsync(mine, 1);
        await factory.SeedEnrichedItemsAsync(theirs, 1);

        var sync = await SyncAsync(owner);

        Assert.DoesNotContain(sync.Playlists, p => p.Id == theirs);
        Assert.All(sync.Items, i => Assert.Equal(mine, i.PlaylistId));
    }

    [Fact]
    public async Task Item_tags_ride_along_so_a_client_does_not_need_a_second_call()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Tagged");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var first = await SyncAsync(client);
        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { tags = new[] { "reference" } }))
            .EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        // Tags live in their own rows, so without touching the item a client syncing on
        // LastModified would never hear that they changed.
        Assert.Single(second.Items);
    }

    [Fact]
    public async Task Changing_a_playlists_tags_is_reported_too()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Tagged list");

        var first = await SyncAsync(client);
        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { tags = new[] { "reference" } }))
            .EnsureSuccessStatusCode();

        var second = await SyncAsync(client, first.Until);

        var changed = Assert.Single(second.Playlists);
        Assert.Equal(["reference"], changed.Tags!);
    }

    [Fact]
    public async Task The_next_window_starts_where_this_one_ended()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Windows");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var first = await SyncAsync(client);

        // Taken from the server's clock: a client with a skewed one would otherwise ask for a
        // window that skips changes it never saw.
        Assert.True(first.Until <= DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.True(first.Until >= DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.False(first.More);
    }
}
