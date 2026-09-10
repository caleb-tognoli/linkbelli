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

    [Fact]
    public async Task A_garbled_cursor_is_treated_as_a_first_page()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bad cursor");
        await factory.SeedEnrichedItemsAsync(playlist, 4);

        var page = await PageAsync(client, playlist, "limit=10&cursor=not-a-cursor");

        Assert.Equal(4, page.Total);
        Assert.Equal(4, page.Items.Count);
    }
}
