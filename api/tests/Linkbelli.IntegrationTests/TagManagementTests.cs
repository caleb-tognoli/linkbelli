using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Tidying tags up.
/// </summary>
/// <remarks>
/// Tags could be applied from four directions — by hand on a playlist, by hand on an item, by an
/// automation rule, and by an import — and removed from exactly none of them. The interesting
/// part is that <c>Tag</c> rows are global and unique on name, so "rename" cannot mean renaming
/// the row: it has to mean repointing this account's joins, which is also what merging is.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class TagManagementTests(PostgresApiFactory factory)
{
    private record TaggedItemDto(Guid Id, string[] Tags);

    private record PagedItemsDto(List<TaggedItemDto> Items);

    private record TagUsageDto(string Name, int PlaylistCount, int ItemCount);

    private record TagChangeDto(int Playlists, int Items, bool Merged);

    private record SyncedItemDto(Guid Id, string[]? Tags);

    private record SyncDto(List<SyncedItemDto> Items);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name, params string[] tags)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, tags });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task TagItemAsync(HttpClient client, Guid itemId, params string[] tags) =>
        (await client.PatchAsJsonAsync($"/api/v1/items/{itemId}", new { tags })).EnsureSuccessStatusCode();

    /// <summary>Read back through the listing: there is no GET for a single item.</summary>
    private static async Task<string[]> ItemTagsAsync(HttpClient client, Guid playlistId, Guid itemId) =>
        (await client.GetFromJsonAsync<PagedItemsDto>($"/api/v1/playlists/{playlistId}/items"))!
            .Items.Single(i => i.Id == itemId).Tags;

    private static async Task<string[]> PlaylistTagsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PlaylistDto>($"/api/v1/playlists/{playlistId}"))!.Tags;

    private static async Task<HttpResponseMessage> RenameAsync(HttpClient client, string from, string to) =>
        await client.PostAsJsonAsync("/api/v1/tags/rename", new { from, to });

    private static async Task<TagChangeDto> RenameOkAsync(HttpClient client, string from, string to)
    {
        var res = await RenameAsync(client, from, to);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<TagChangeDto>())!;
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, string name) =>
        await client.PostAsJsonAsync("/api/v1/tags/delete", new { name });

    /// <summary>A name nobody else's test has used, so counts are assertable on a shared database.</summary>
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    [Fact]
    public async Task Renaming_moves_the_tag_on_playlists_and_items_alike()
    {
        var client = await NewUserAsync();
        var old = Unique("ml");
        var wanted = Unique("machine-learning");

        var playlist = await NewPlaylistAsync(client, "Papers", old);
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);
        await TagItemAsync(client, seeded[0], old);

        var change = await RenameOkAsync(client, old, wanted);

        Assert.Equal(1, change.Playlists);
        Assert.Equal(1, change.Items);
        Assert.False(change.Merged);
        Assert.Equal([wanted], await PlaylistTagsAsync(client, playlist));
        Assert.Equal([wanted], await ItemTagsAsync(client, playlist, seeded[0]));
    }

    /// <summary>
    /// The case the feature exists for: two names for one subject, collapsed into one.
    /// </summary>
    [Fact]
    public async Task Renaming_onto_a_tag_you_already_use_merges_into_it()
    {
        var client = await NewUserAsync();
        var loser = Unique("js");
        var winner = Unique("javascript");

        var playlist = await NewPlaylistAsync(client, "Front end");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);
        await TagItemAsync(client, seeded[0], loser);
        await TagItemAsync(client, seeded[1], winner);

        var change = await RenameOkAsync(client, loser, winner);

        // Said out loud, because "merged into a tag you already had" is a different thing to
        // the person asking than "renamed to a fresh one".
        Assert.True(change.Merged);
        Assert.Equal([winner], await ItemTagsAsync(client, playlist, seeded[0]));
        Assert.Equal([winner], await ItemTagsAsync(client, playlist, seeded[1]));
    }

    /// <summary>
    /// The row that carries both. Repointing it would collide with the unique pair index, so it
    /// has to be dropped instead — and the item must still end up carrying the tag exactly once.
    /// </summary>
    [Fact]
    public async Task Merging_an_item_that_already_has_both_leaves_one_tag_not_two()
    {
        var client = await NewUserAsync();
        var loser = Unique("k8s");
        var winner = Unique("kubernetes");
        var keep = Unique("ops");

        var playlist = await NewPlaylistAsync(client, "Infra", loser, winner);
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);
        await TagItemAsync(client, seeded[0], loser, winner, keep);

        await RenameOkAsync(client, loser, winner);

        Assert.Equal(new[] { winner, keep }.Order(), (await ItemTagsAsync(client, playlist, seeded[0])).Order());
        Assert.Equal([winner], await PlaylistTagsAsync(client, playlist));
    }

    [Fact]
    public async Task Deleting_removes_every_use_and_leaves_the_others_alone()
    {
        var client = await NewUserAsync();
        var doomed = Unique("typo");
        var keep = Unique("keep");

        var playlist = await NewPlaylistAsync(client, "Mixed", doomed, keep);
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);
        await TagItemAsync(client, seeded[0], doomed, keep);

        var res = await DeleteAsync(client, doomed);
        res.EnsureSuccessStatusCode();
        var change = (await res.Content.ReadFromJsonAsync<TagChangeDto>())!;

        Assert.Equal(1, change.Playlists);
        Assert.Equal(1, change.Items);
        Assert.Equal([keep], await PlaylistTagsAsync(client, playlist));
        Assert.Equal([keep], await ItemTagsAsync(client, playlist, seeded[0]));
    }

    /// <summary>
    /// The one that matters most. <c>Tag</c> rows are shared by everybody, so an unscoped rename
    /// or delete would reach into other people's libraries.
    /// </summary>
    [Fact]
    public async Task Renaming_a_tag_you_share_with_a_stranger_only_touches_your_own()
    {
        var mine = await NewUserAsync();
        var theirs = await NewUserAsync();
        var shared = Unique("rust");

        var myPlaylist = await NewPlaylistAsync(mine, "Mine", shared);
        var theirPlaylist = await NewPlaylistAsync(theirs, "Theirs", shared);

        var change = await RenameOkAsync(mine, shared, Unique("rust-lang"));

        Assert.Equal(1, change.Playlists);
        Assert.Equal([shared], await PlaylistTagsAsync(theirs, theirPlaylist));
        Assert.DoesNotContain(shared, await PlaylistTagsAsync(mine, myPlaylist));
    }

    /// <summary>
    /// A tag that exists only in somebody else's library reads as "no such tag" rather than as a
    /// permission error — anything else would tell you what strangers have tagged things with.
    /// </summary>
    [Fact]
    public async Task A_tag_only_a_stranger_uses_is_not_yours_to_rename()
    {
        var mine = await NewUserAsync();
        var theirs = await NewUserAsync();
        var shared = Unique("theirs");

        await NewPlaylistAsync(theirs, "Theirs", shared);

        Assert.Equal(HttpStatusCode.NotFound, (await RenameAsync(mine, shared, "mine")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(mine, shared)).StatusCode);
    }

    [Fact]
    public async Task A_tag_that_does_not_exist_is_a_404()
    {
        var client = await NewUserAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await RenameAsync(client, Unique("ghost"), "real")).StatusCode);
    }

    /// <summary>
    /// Normalization lowercases, so this asks for a state that already holds. That is a no-op,
    /// not an error — and specifically must not delete the tag by repointing it at itself.
    /// </summary>
    [Fact]
    public async Task Renaming_a_tag_to_its_own_name_in_different_case_changes_nothing()
    {
        var client = await NewUserAsync();
        var tag = Unique("design");
        var playlist = await NewPlaylistAsync(client, "Case", tag);

        var change = await RenameOkAsync(client, tag, tag.ToUpperInvariant());

        Assert.Equal(0, change.Playlists);
        Assert.Equal([tag], await PlaylistTagsAsync(client, playlist));
    }

    [Fact]
    public async Task Renaming_to_nothing_is_rejected_rather_than_treated_as_a_delete()
    {
        var client = await NewUserAsync();
        var tag = Unique("kept");
        var playlist = await NewPlaylistAsync(client, "Not deleted", tag);

        Assert.Equal(HttpStatusCode.BadRequest, (await RenameAsync(client, tag, "   ")).StatusCode);
        Assert.Equal([tag], await PlaylistTagsAsync(client, playlist));
    }

    /// <summary>
    /// The usage list is what a destructive bulk edit gets confirmed against, and GET /tags only
    /// ever counted playlists — so a tag used on fifty items and no playlists looked unused.
    /// </summary>
    [Fact]
    public async Task Usage_counts_items_as_well_as_playlists()
    {
        var client = await NewUserAsync();
        var onItemsOnly = Unique("itemsonly");
        var onBoth = Unique("both");

        var playlist = await NewPlaylistAsync(client, "Counted", onBoth);
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);
        await TagItemAsync(client, seeded[0], onItemsOnly, onBoth);
        await TagItemAsync(client, seeded[1], onItemsOnly);

        var usage = (await client.GetFromJsonAsync<List<TagUsageDto>>("/api/v1/tags/usage"))!;

        var items = usage.Single(t => t.Name == onItemsOnly);
        Assert.Equal(0, items.PlaylistCount);
        Assert.Equal(2, items.ItemCount);

        var both = usage.Single(t => t.Name == onBoth);
        Assert.Equal(1, both.PlaylistCount);
        Assert.Equal(1, both.ItemCount);
    }

    [Fact]
    public async Task A_deleted_tag_disappears_from_the_usage_list()
    {
        var client = await NewUserAsync();
        var tag = Unique("gone");

        var playlist = await NewPlaylistAsync(client, "Emptied", tag);
        (await DeleteAsync(client, tag)).EnsureSuccessStatusCode();

        var usage = (await client.GetFromJsonAsync<List<TagUsageDto>>("/api/v1/tags/usage"))!;

        Assert.DoesNotContain(usage, t => t.Name == tag);
        Assert.Empty(await PlaylistTagsAsync(client, playlist));
    }

    /// <summary>
    /// Tags live in their own rows, so a rename leaves the item row untouched and a client
    /// syncing on LastModified would hand back the old tags forever.
    /// </summary>
    [Fact]
    public async Task A_rename_is_visible_to_a_syncing_client()
    {
        var client = await NewUserAsync();
        var old = Unique("before");
        var wanted = Unique("after");

        var playlist = await NewPlaylistAsync(client, "Synced");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);
        await TagItemAsync(client, seeded[0], old);

        // A client that has already seen everything up to this moment.
        var since = DateTimeOffset.UtcNow;
        await RenameOkAsync(client, old, wanted);

        var sync = await client.GetFromJsonAsync<SyncDto>(
            $"/api/v1/sync?since={Uri.EscapeDataString(since.ToString("O"))}");

        var item = Assert.Single(sync!.Items, i => i.Id == seeded[0]);
        Assert.Equal([wanted], item.Tags!);
    }
}
