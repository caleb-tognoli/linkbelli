using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Putting something aside.
/// </summary>
/// <remarks>
/// Ordering the queue by age stops new arrivals burying old ones, and does nothing about the item
/// offered forty times and skipped forty times — which is the actual way a backlog becomes
/// permanent. "Not now" was the missing verb, and it only means anything if the thing actually
/// goes away for a while.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class SnoozeTests(PostgresApiFactory factory)
{
    private record ItemDto(Guid Id, DateTimeOffset? SnoozedUntil, int SnoozeCount);

    private record HitDto(Guid ItemId, DateTimeOffset? SnoozedUntil, int SnoozeCount);

    private record SearchPage(List<HitDto> Items, int? Total);

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

    private static Task<HttpResponseMessage> SnoozeAsync(HttpClient client, Guid itemId, object body) =>
        client.PostAsJsonAsync($"/api/v1/items/{itemId}/snooze", body);

    private static async Task<SearchPage> QueueAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SearchPage>(
            "/api/v1/search?status=unwatched&sort=queue&limit=100"))!;

    private static async Task<SearchPage> SnoozedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<SearchPage>("/api/v1/search?snoozed=true&limit=100"))!;

    [Fact]
    public async Task Something_put_aside_leaves_the_queue()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Queue {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        Assert.Equal(3, (await QueueAsync(client)).Items.Count(h => seeded.Contains(h.ItemId)));

        (await SnoozeAsync(client, seeded[0], new { preset = "weekend" })).EnsureSuccessStatusCode();

        var queue = await QueueAsync(client);
        Assert.Equal(2, queue.Items.Count(h => seeded.Contains(h.ItemId)));
        Assert.DoesNotContain(queue.Items, h => h.ItemId == seeded[0]);
    }

    /// <summary>Gone from search too, or "not now" is a button that changes nothing.</summary>
    [Fact]
    public async Task It_leaves_search_as_well_as_the_queue()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Searched");
        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"{marker} {n}");

        (await SnoozeAsync(client, seeded[0], new { preset = "month" })).EnsureSuccessStatusCode();

        var found = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={marker}");

        Assert.Equal(1, found!.Total);
        Assert.Equal(seeded[1], Assert.Single(found.Items).ItemId);
    }

    [Fact]
    public async Task Asking_for_them_specifically_is_how_they_come_back()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Put aside");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        (await SnoozeAsync(client, seeded[0], new { preset = "week" })).EnsureSuccessStatusCode();

        var aside = await SnoozedAsync(client);

        var hit = Assert.Single(aside.Items, h => h.ItemId == seeded[0]);
        Assert.NotNull(hit.SnoozedUntil);
        Assert.Equal(1, hit.SnoozeCount);
    }

    /// <summary>
    /// An item put aside repeatedly is a signal, and a client can only offer to get rid of it if
    /// the count is kept.
    /// </summary>
    [Fact]
    public async Task Putting_something_aside_again_counts()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Again and again");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        for (var i = 0; i < 3; i++)
        {
            (await SnoozeAsync(client, seeded[0], new { preset = "tomorrow" })).EnsureSuccessStatusCode();
        }

        Assert.Equal(3, Assert.Single((await SnoozedAsync(client)).Items, h => h.ItemId == seeded[0]).SnoozeCount);
    }

    [Fact]
    public async Task It_can_be_brought_back_before_it_is_due()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Woken {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        (await SnoozeAsync(client, seeded[0], new { preset = "month" })).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await QueueAsync(client)).Items, h => h.ItemId == seeded[0]);

        var woken = await client.DeleteAsync($"/api/v1/items/{seeded[0]}/snooze");
        woken.EnsureSuccessStatusCode();

        Assert.Contains((await QueueAsync(client)).Items, h => h.ItemId == seeded[0]);

        // The count survives waking it. How often it has been put aside is still true.
        Assert.Equal(1, (await woken.Content.ReadFromJsonAsync<ItemDto>())!.SnoozeCount);
    }

    /// <summary>
    /// The resurface half: a moment that arrives is a moment that brings it back, with nothing
    /// needing to run in between.
    /// </summary>
    [Fact]
    public async Task A_moment_that_has_passed_brings_it_back_on_its_own()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Due {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        (await SnoozeAsync(client, seeded[0], new { until = DateTimeOffset.UtcNow.AddSeconds(2) }))
            .EnsureSuccessStatusCode();

        Assert.DoesNotContain((await QueueAsync(client)).Items, h => h.ItemId == seeded[0]);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.Contains((await QueueAsync(client)).Items, h => h.ItemId == seeded[0]);
    }

    [Fact]
    public async Task A_moment_already_gone_is_refused()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Backwards");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var res = await SnoozeAsync(client, seeded[0], new { until = DateTimeOffset.UtcNow.AddDays(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_preset_nobody_defined_says_which_ones_exist()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bad preset");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var res = await SnoozeAsync(client, seeded[0], new { preset = "next tuesday" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("weekend", body);
    }

    [Fact]
    public async Task Somebody_elses_item_is_not_yours_to_put_aside()
    {
        var mine = await NewUserAsync();
        var theirs = await NewUserAsync();
        var playlist = await NewPlaylistAsync(theirs, "Theirs");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var res = await SnoozeAsync(mine, seeded[0], new { preset = "week" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
