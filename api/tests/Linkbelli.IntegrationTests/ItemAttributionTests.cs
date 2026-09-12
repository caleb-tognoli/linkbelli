using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Who put a link in a playlist.
/// </summary>
/// <remarks>
/// Nothing recorded it, on a product with playlist membership and roles — so on a list three
/// people contribute to, "who saved this?" had no answer and neither did "show me only mine".
/// SourceId already answered the machine version of the same question, which is what made the
/// absence of this one look like an oversight rather than a decision.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ItemAttributionTests(PostgresApiFactory factory)
{
    private record ItemDto(Guid Id, LinkDto Link, string? AddedBy);

    private record LinkDto(Guid Id, string Url);

    private record PagedItems(List<ItemDto> Items);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));
        return (client, username);
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    /// <summary>
    /// Adds a link the way a person does, then marks it enriched.
    /// </summary>
    /// <remarks>
    /// Listing only returns enriched items, and enrichment is a background fetch that will not
    /// resolve an .example host. Adding through the API is the part that matters here — it is
    /// what sets the attribution — so the fetch is stood in for rather than waited on.
    /// </remarks>
    private async Task<HttpResponseMessage> AddAsync(HttpClient client, Guid playlist, string url)
    {
        var res = await client.PostAsJsonAsync($"/api/v1/playlists/{playlist}/items", new { url });
        await EnrichAllAsync();
        return res;
    }

    private async Task EnrichAllAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        await db.Links
            .Where(l => l.EnrichedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(l => l.EnrichedAt, DateTimeOffset.UtcNow));
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlist) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items;

    [Fact]
    public async Task A_link_you_add_is_recorded_as_yours()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mine");

        (await AddAsync(client, playlist, $"https://a{Guid.NewGuid():N}.example/")).EnsureSuccessStatusCode();

        var item = Assert.Single(await ItemsAsync(client, playlist));
        Assert.Equal(username, item.AddedBy, ignoreCase: true);
    }

    /// <summary>The question the column exists for.</summary>
    [Fact]
    public async Task A_shared_playlist_says_which_of_you_added_what()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (guest, guestName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Ours");

        var shared = await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist}/members/{guestName}", new { role = "Editor" });
        shared.EnsureSuccessStatusCode();

        var mine = $"https://owner{Guid.NewGuid():N}.example/";
        var theirs = $"https://guest{Guid.NewGuid():N}.example/";
        (await AddAsync(owner, playlist, mine)).EnsureSuccessStatusCode();
        (await AddAsync(guest, playlist, theirs)).EnsureSuccessStatusCode();

        var items = await ItemsAsync(owner, playlist);

        Assert.Equal(ownerName, items.Single(i => i.Link.Url == mine).AddedBy, ignoreCase: true);
        Assert.Equal(guestName, items.Single(i => i.Link.Url == theirs).AddedBy, ignoreCase: true);
    }

    [Fact]
    public async Task Pasting_a_block_of_addresses_records_you_too()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Pasted");

        var res = await client.PostAsJsonAsync($"/api/v1/playlists/{playlist}/items/paste", new
        {
            text = $"https://p{Guid.NewGuid():N}.example/ and https://q{Guid.NewGuid():N}.example/",
        });
        res.EnsureSuccessStatusCode();
        await EnrichAllAsync();

        var items = await ItemsAsync(client, playlist);
        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal(username, i.AddedBy, ignoreCase: true));
    }

    /// <summary>
    /// A copy is the copier's act, even though the note and score come across with it.
    /// </summary>
    /// <remarks>
    /// The source row here is seeded with no attribution — the state everything saved before this
    /// column is in — so the copy cannot pass by inheriting an answer. If it comes out named, the
    /// name came from the person doing the copying, which is the thing being asserted.
    ///
    /// Both playlists belong to the same person because bulk actions are owner-scoped: they only
    /// touch items in playlists the caller owns, which is a deliberate restriction and not one to
    /// work around in a test.
    /// </remarks>
    [Fact]
    public async Task Copying_a_link_records_whoever_copied_it()
    {
        var (client, username) = await NewUserAsync();
        var source = await NewPlaylistAsync(client, "From");
        var destination = await NewPlaylistAsync(client, "To");

        await factory.SeedEnrichedItemsAsync(source, 1);
        var original = Assert.Single(await ItemsAsync(client, source));
        Assert.Null(original.AddedBy);

        var res = await client.PostAsJsonAsync("/api/v1/items/bulk", new
        {
            itemIds = new[] { original.Id },
            action = "Copy",
            targetPlaylistId = destination,
        });
        res.EnsureSuccessStatusCode();

        var copied = Assert.Single(await ItemsAsync(client, destination));
        Assert.Equal(username, copied.AddedBy, ignoreCase: true);
    }

    /// <summary>
    /// Items a source created have no person behind them, and SourceId is the honest answer
    /// there — inventing one would be worse than admitting there isn't one.
    /// </summary>
    [Fact]
    public async Task A_link_that_predates_this_has_nobody_attached_rather_than_a_guess()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Seeded");

        // Seeded straight into the database, the way every row created before this column was.
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        var item = Assert.Single(await ItemsAsync(client, playlist));
        Assert.Null(item.AddedBy);
    }
}
