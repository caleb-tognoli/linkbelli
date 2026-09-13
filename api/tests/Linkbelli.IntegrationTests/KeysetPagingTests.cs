using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Paging that does not shift under the reader.
/// </summary>
/// <remarks>
/// The "opaque cursor" was a base64 offset — <c>MQ==</c> decoded to <c>1</c>, and
/// <c>cursor=OTk5OTk5OTk5</c> was happily accepted as offset 999,999,999. Paging by counting rows
/// means every insert above the reader's position pushes the whole list down by one, so the next
/// page hands back a row that was already on the last one. On an infinite-scroll list that is
/// visible: the same playlist, twice.
///
/// These tests write between the two requests, which is the only way to tell the two schemes
/// apart — with nothing changing, an offset is indistinguishable from a position.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class KeysetPagingTests(PostgresApiFactory factory)
{
    private record ItemsPage(List<PlaylistDto> Items, string? NextCursor);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<ItemsPage> PageAsync(HttpClient client, int limit, string? cursor = null)
    {
        var url = $"/api/v1/playlists?limit={limit}&unfiled=true"
            + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ItemsPage>())!;
    }

    /// <summary>
    /// The bug, reproduced and then not reproduced.
    /// </summary>
    /// <remarks>
    /// Four playlists, read two at a time. Between the pages, a fifth is created — which sorts to
    /// the top, because this list is ordered by recent activity. Under an offset cursor the second
    /// page starts at row three of a list that now has five, so it returns the second-oldest
    /// playlist again. Under a position cursor it resumes after the row it actually stopped at.
    /// </remarks>
    [Fact]
    public async Task Inserting_a_row_between_pages_does_not_repeat_one()
    {
        var client = await NewUserAsync();
        for (var i = 0; i < 4; i++)
        {
            await NewPlaylistAsync(client, $"Page {i}");
        }

        var first = await PageAsync(client, 2);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);

        // The write that used to break it.
        await NewPlaylistAsync(client, "Arrived mid-scroll");

        var second = await PageAsync(client, 2, first.NextCursor);

        var seen = first.Items.Concat(second.Items).Select(p => p.Id).ToList();
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    /// <summary>
    /// The other half of the same bug: a deletion pulls the list up, so an offset page steps over
    /// a row nobody ever saw.
    /// </summary>
    [Fact]
    public async Task Deleting_a_row_between_pages_does_not_skip_one()
    {
        var client = await NewUserAsync();
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await NewPlaylistAsync(client, $"Kept {i}"));
        }

        var first = await PageAsync(client, 2);
        Assert.Equal(2, first.Items.Count);

        // Delete one the reader has already gone past.
        (await client.DeleteAsync($"/api/v1/playlists/{first.Items[0].Id}")).EnsureSuccessStatusCode();

        var seen = new List<Guid>(first.Items.Select(p => p.Id));
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await PageAsync(client, 2, cursor);
            seen.AddRange(page.Items.Select(p => p.Id));
            cursor = page.NextCursor;
        }

        // Nothing twice, and nothing missed. The deleted playlist is legitimately in this list —
        // it was on the first page, before it was deleted — so what is asserted is that every
        // playlist that still exists was handed over, which is what an offset page stops doing
        // the moment a row above the reader disappears.
        Assert.Equal(seen.Count, seen.Distinct().Count());

        foreach (var id in created.Where(id => id != first.Items[0].Id))
        {
            Assert.Contains(id, seen);
        }
    }

    /// <summary>
    /// Playlists created in the same tick — which an import does routinely — must not straddle a
    /// page break invisibly. This is why the position carries the id as well as the time.
    /// </summary>
    [Fact]
    public async Task Rows_sharing_a_timestamp_are_all_handed_over()
    {
        var client = await NewUserAsync();
        var created = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            created.Add(await NewPlaylistAsync(client, $"Tied {i}"));
        }

        // Every playlist given the same LastActivity, so the timestamp alone cannot separate them.
        await factory.SettleActivityAsync(created);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(client, 2, cursor);
            seen.AddRange(page.Items.Select(p => p.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(created.Count, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(99999)]
    public async Task A_limit_outside_the_range_is_refused_rather_than_corrected(int limit)
    {
        var client = await NewUserAsync();

        var res = await client.GetAsync($"/api/v1/playlists?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_cursor_from_nowhere_is_refused_rather_than_treated_as_page_one()
    {
        var client = await NewUserAsync();

        // What the old scheme accepted without blinking: base64 of an enormous row count.
        var res = await client.GetAsync("/api/v1/playlists?cursor=OTk5OTk5OTk5");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// The feed is the listing where this matters most: everybody followed keeps adding to it, so
    /// something lands above the reader's position every few seconds.
    /// </summary>
    [Fact]
    public async Task The_feed_pages_by_position_too()
    {
        var authorName = NewUsername();
        var author = factory.CreateClient();
        author.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await author.RegisterAndLoginAsync(authorName));

        var reader = await NewUserAsync();

        var res = await author.PostAsJsonAsync(
            "/api/v1/playlists", new { name = "Published", visibility = "Public" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        (await reader.PostAsync($"/api/v1/users/{authorName}/follow", null)).EnsureSuccessStatusCode();

        await factory.SeedEnrichedItemsAsync(playlist, 4);

        var first = await reader.GetFromJsonAsync<FeedPage>("/api/v1/feed?limit=2");
        Assert.Equal(2, first!.Items.Count);
        Assert.NotNull(first.NextCursor);

        // Another link arrives while the reader is part way down.
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        var second = await reader.GetFromJsonAsync<FeedPage>(
            $"/api/v1/feed?limit=2&cursor={Uri.EscapeDataString(first.NextCursor)}");

        var seen = first.Items.Concat(second!.Items).Select(i => i.ItemId).ToList();
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    private record FeedEntry(Guid ItemId);

    private record FeedPage(List<FeedEntry> Items, string? NextCursor);
}
