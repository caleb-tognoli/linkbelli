using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// How far through something somebody got.
/// </summary>
/// <remarks>
/// Status had two values, so an article was either untouched or finished. A twenty-two-minute
/// piece read half of on the train was indistinguishable from one never opened: "Up next" kept
/// offering it from the top, and the only way to clear it was to lie by marking it watched.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ReadProgressTests(PostgresApiFactory factory)
{
    private record ContentDto(Guid Id, string Url, int WordCount, double? ReadProgress);

    private record ItemDto(Guid Id, string Status, LinkRef Link, double? ReadProgress);

    private record LinkRef(Guid Id);

    private record PagedItems(List<ItemDto> Items);

    private record HitDto(Guid ItemId, double? ReadProgress);

    private record SearchPage(List<HitDto> Items);

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

    /// <summary>A link with article text behind it, which is what the reader needs.</summary>
    private async Task<(Guid ItemId, Guid LinkId)> ReadableItemAsync(HttpClient client, Guid playlist)
    {
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);
        await factory.GiveArticleTextAsync(seeded[0]);

        var items = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        var item = items!.Items.Single(i => i.Id == seeded[0]);

        return (item.Id, item.Link.Id);
    }

    private static async Task<HttpResponseMessage> SetAsync(HttpClient client, Guid linkId, double progress) =>
        await client.PutAsJsonAsync($"/api/v1/links/{linkId}/progress", new { progress });

    private static async Task<ItemDto> ItemAsync(HttpClient client, Guid playlist, Guid itemId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!
            .Items.Single(i => i.Id == itemId);

    [Fact]
    public async Task Half_read_is_recorded_and_read_back()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        (await SetAsync(client, linkId, 0.5)).EnsureSuccessStatusCode();

        Assert.Equal(0.5, (await ItemAsync(client, playlist, itemId)).ReadProgress);

        // And the reader itself, which is what opens where the reading stopped.
        var content = await client.GetFromJsonAsync<ContentDto>($"/api/v1/links/{linkId}/content");
        Assert.Equal(0.5, content!.ReadProgress);
    }

    /// <summary>
    /// Scrolling up to re-read a paragraph is not un-reading it, and a progress bar that retreats
    /// while you look at it is worse than none.
    /// </summary>
    [Fact]
    public async Task Progress_never_goes_backwards()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Backwards");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        (await SetAsync(client, linkId, 0.6)).EnsureSuccessStatusCode();
        (await SetAsync(client, linkId, 0.2)).EnsureSuccessStatusCode();

        Assert.Equal(0.6, (await ItemAsync(client, playlist, itemId)).ReadProgress);
    }

    /// <summary>
    /// The step this exists to remove. Reaching the end of something is what finishing it means;
    /// making somebody say so as well is a step for the app's benefit.
    /// </summary>
    [Fact]
    public async Task Reaching_the_end_marks_it_finished()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Finished");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        Assert.Equal("Added", (await ItemAsync(client, playlist, itemId)).Status);

        (await SetAsync(client, linkId, 1)).EnsureSuccessStatusCode();

        Assert.Equal("Watched", (await ItemAsync(client, playlist, itemId)).Status);
    }

    /// <summary>Not 1.0: every article ends in a footer nobody reads.</summary>
    [Fact]
    public async Task Nearly_the_end_is_the_end()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Nearly");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        (await SetAsync(client, linkId, 0.93)).EnsureSuccessStatusCode();

        Assert.Equal("Watched", (await ItemAsync(client, playlist, itemId)).Status);
    }

    [Fact]
    public async Task Part_way_through_does_not_mark_it_finished()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Part way");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        (await SetAsync(client, linkId, 0.5)).EnsureSuccessStatusCode();

        Assert.Equal("Added", (await ItemAsync(client, playlist, itemId)).Status);
    }

    /// <summary>
    /// A link in two playlists is one article. Reading it in one place must not leave the other
    /// half-read, which would be a worse lie than the two-state flag this replaces.
    /// </summary>
    [Fact]
    public async Task Reading_one_copy_advances_every_copy()
    {
        var client = await NewUserAsync();
        var first = await NewPlaylistAsync(client, "Here");
        var second = await NewPlaylistAsync(client, "And here");

        var url = $"https://both{Guid.NewGuid():N}.example/article";
        var a = await factory.SeedEnrichedItemsAsync(first, 1, url: _ => url);
        var b = await factory.SeedEnrichedItemsAsync(second, 1, url: _ => url);
        await factory.GiveArticleTextAsync(a[0]);

        var linkId = (await ItemAsync(client, first, a[0])).Link.Id;
        (await SetAsync(client, linkId, 0.4)).EnsureSuccessStatusCode();

        Assert.Equal(0.4, (await ItemAsync(client, first, a[0])).ReadProgress);
        Assert.Equal(0.4, (await ItemAsync(client, second, b[0])).ReadProgress);
    }

    /// <summary>
    /// Reading is only ever offered from somebody's own library, so a link they have not saved is
    /// either a stale tab or somebody guessing at ids.
    /// </summary>
    [Fact]
    public async Task A_link_you_have_not_saved_is_not_yours_to_be_reading()
    {
        var mine = await NewUserAsync();
        var theirs = await NewUserAsync();
        var playlist = await NewPlaylistAsync(theirs, "Theirs");
        var (_, linkId) = await ReadableItemAsync(theirs, playlist);

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(mine, linkId, 0.5)).StatusCode);
    }

    [Fact]
    public async Task Out_of_range_progress_is_pinned_to_the_range()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Silly numbers");
        var (itemId, linkId) = await ReadableItemAsync(client, playlist);

        (await SetAsync(client, linkId, 42)).EnsureSuccessStatusCode();
        Assert.Equal(1, (await ItemAsync(client, playlist, itemId)).ReadProgress);
    }

    /// <summary>
    /// The point of recording it: something started is the cheapest thing to finish, so it leads
    /// the queue instead of sitting behind everything that was never opened.
    /// </summary>
    [Fact]
    public async Task The_queue_puts_what_you_started_first()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Queue {Guid.NewGuid():N}");

        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);
        var items = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        var started = items!.Items.Single(i => i.Id == seeded[2]);

        (await SetAsync(client, started.Link.Id, 0.5)).EnsureSuccessStatusCode();

        var queue = await client.GetFromJsonAsync<SearchPage>(
            "/api/v1/search?status=unwatched&sort=queue&limit=50");

        var mine = queue!.Items.Where(h => seeded.Contains(h.ItemId)).ToList();
        Assert.Equal(seeded[2], mine[0].ItemId);
    }
}
