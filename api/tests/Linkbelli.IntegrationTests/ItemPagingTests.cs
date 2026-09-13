using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Paging over a playlist's items. The total is counted once and then carried inside the cursor,
/// so these assert it stays correct across every page and every sort mode.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemPagingTests(PostgresApiFactory factory)
{
    private record PagedItemsDto(List<ItemDto> Items, string? NextCursor, int? Total);

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

    private static async Task<PagedItemsDto> PageAsync(HttpClient client, Guid playlistId, string query)
    {
        var res = await client.GetAsync($"/api/v1/playlists/{playlistId}/items?{query}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PagedItemsDto>())!;
    }

    [Theory]
    [InlineData("position")]
    [InlineData("date-desc")]
    [InlineData("date-asc")]
    [InlineData("score-desc")]
    [InlineData("shuffle")]
    public async Task Total_is_reported_on_every_page_of_every_sort(string sort)
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Paging {sort}");
        await factory.SeedEnrichedItemsAsync(playlist, 7);

        var seen = new HashSet<Guid>();
        var pages = 0;
        string? cursor = null;

        do
        {
            var page = await PageAsync(client, playlist,
                $"limit=3&sort={sort}" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));

            Assert.Equal(7, page.Total);
            foreach (var item in page.Items) seen.Add(item.Id);

            cursor = page.NextCursor;
            pages++;
            Assert.True(pages <= 5, "Paging did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(3, pages);          // 3 + 3 + 1
        Assert.Equal(7, seen.Count);     // every item seen exactly once across pages
    }

    [Fact]
    public async Task Total_reflects_the_active_filter_not_the_whole_playlist()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Filtered total");
        await factory.SeedEnrichedItemsAsync(playlist, 5, title: n => n < 2 ? $"Postgres {n}" : $"Unrelated {n}");

        var page = await PageAsync(client, playlist, "limit=10&q=postgres");

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
    }

    /// <summary>
    /// A garbled cursor is refused, where it used to be quietly treated as a first page.
    /// </summary>
    /// <remarks>
    /// This test previously asserted the opposite, and the old behaviour is what it was written
    /// against. Silently restarting hides the client bug that produced the bad cursor — the
    /// reader sees the top of the list again and assumes they scrolled wrong — and on a shuffled
    /// playlist it deals the same links out in a new order with nothing to say it happened.
    /// </remarks>
    [Theory]
    // Not base64.
    [InlineData("not-a-cursor")]
    // Base64, but not one of ours: no carried total.
    [InlineData("MTUw")]
    public async Task A_garbled_cursor_is_refused_rather_than_restarting(string cursor)
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bad cursor");
        await factory.SeedEnrichedItemsAsync(playlist, 4);

        var res = await client.GetAsync(
            $"/api/v1/playlists/{playlist}/items?limit=10&cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// An absent cursor is still a first page — <c>?cursor=</c> comes off a form as readily as
    /// it comes off a bug, and asking for the start is not an error.
    /// </summary>
    [Fact]
    public async Task An_empty_cursor_is_still_a_first_page()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "No cursor");
        await factory.SeedEnrichedItemsAsync(playlist, 4);

        var page = await PageAsync(client, playlist, "limit=10&cursor=");

        Assert.Equal(4, page.Total);
        Assert.Equal(4, page.Items.Count);
    }
}
