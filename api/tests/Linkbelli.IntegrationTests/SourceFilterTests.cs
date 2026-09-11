using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Sources;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Everything a source found used to land unconditionally, which made subscribing to a broad feed
/// an all-or-nothing decision. These cover what gets through, what gets counted, and the one case
/// nothing else could express: telling a source to stop bringing back what you deleted.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceFilterApiTests(PostgresApiFactory factory)
{
    /// <summary>Stands in for the RSS interpreter, returning exactly what a test asks it for.</summary>
    private sealed class ScriptedInterpreter : ISourceInterpreter
    {
        public static List<DiscoveredLink> Next { get; set; } = [];

        public SourceType Type => SourceType.Rss;

        public void ValidateConfig(IReadOnlyDictionary<string, string> config) { }

        public Task<SourceFetchResult> FetchAsync(
            IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceFetchResult(Next));
    }

    private record SourceDto(Guid Id, string Name, SourceFilter? Filter, Guid[] PlaylistIds);

    private record RunDto(Guid Id, string Status, int FoundCount, int AddedCount, int SkippedCount);

    private record PlaylistDto(Guid Id, string Name);

    private WebApplicationFactory<Program> WithScripted() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISourceInterpreter>();
            services.AddSingleton<ISourceInterpreter, ScriptedInterpreter>();
        }));

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Filtered" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private static async Task<HttpResponseMessage> CreateSourceAsync(
        HttpClient client, object? filter = null, Guid[]? playlistIds = null) =>
        await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Filtered source",
            type = "Rss",
            config = new { feedUrl = $"https://filtered.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 * * * *",
            playlistIds = playlistIds ?? [],
            filter,
        });

    private static async Task<SourceDto> NewSourceAsync(
        HttpClient client, object? filter = null, Guid[]? playlistIds = null)
    {
        var res = await CreateSourceAsync(client, filter, playlistIds);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SourceDto>())!;
    }

    private static async Task RunAsync(WebApplicationFactory<Program> app, Guid sourceId, params DiscoveredLink[] links)
    {
        ScriptedInterpreter.Next = [.. links];

        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISourceRunner>().RunAsync(sourceId);
    }

    private static async Task<RunDto> LastRunAsync(HttpClient client, Guid sourceId) =>
        (await client.GetFromJsonAsync<List<RunDto>>($"/api/v1/sources/{sourceId}/runs"))![0];

    /// <summary>
    /// Unique per test. Links are global to the application, so a slug another test already used
    /// would come back as "found but not added" and quietly change what AddedCount means here.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N");

    private DiscoveredLink Link(string slug, string? title = null, DateTimeOffset? published = null)
    {
        var meta = new Dictionary<string, string>();
        if (title is not null) meta["title"] = title;
        if (published is { } date) meta[RssSourceInterpreter.PublishedKey] = date.ToUniversalTime().ToString("O");

        return new DiscoveredLink($"https://filtered.example/{_run}/{slug}", null, meta.Count > 0 ? meta : null);
    }

    [Fact]
    public async Task A_filter_survives_the_round_trip()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);

        var source = await NewSourceAsync(client, new { titleExclude = "sponsored", maxItems = 5 });

        Assert.Equal("sponsored", source.Filter?.TitleExclude);
        Assert.Equal(5, source.Filter?.MaxItems);
        Assert.Null(source.Filter?.TitleInclude);
    }

    [Fact]
    public async Task A_filter_that_would_change_nothing_is_stored_as_none_at_all()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);

        var source = await NewSourceAsync(client, new { titleInclude = "   " });

        Assert.Null(source.Filter);
    }

    [Fact]
    public async Task A_pattern_that_will_not_compile_is_refused_at_save_time()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);

        var res = await CreateSourceAsync(client, new { titleInclude = "(" });

        // Told to the person who typed it, rather than to a log at 3am when the run fails.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_window_longer_than_the_trash_keeps_is_refused()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);

        var res = await CreateSourceAsync(
            client, new { dedupeWindowDays = SourceFilter.MaxDedupeWindowDays + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Only_matching_links_are_ingested_and_the_rest_are_counted()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var playlist = await NewPlaylistAsync(client);
        var source = await NewSourceAsync(client, new { titleExclude = "sponsored" }, [playlist.Id]);

        await RunAsync(app, source.Id,
            Link("a", "A real post"),
            Link("b", "Buy this [sponsored]"),
            Link("c", "Another real post"));

        var run = await LastRunAsync(client, source.Id);
        Assert.Equal(2, run.FoundCount);
        Assert.Equal(2, run.AddedCount);
        // Counted, not silently dropped: a strict filter and a broken selector otherwise look
        // identical — both succeed and add nothing.
        Assert.Equal(1, run.SkippedCount);

        Assert.Equal(2, (await ItemIdsAsync(playlist.Id)).Count);
    }

    [Fact]
    public async Task The_cap_keeps_the_items_that_matched_not_the_first_ones_listed()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client, new { titleInclude = "keep", maxItems = 2 });

        await RunAsync(app, source.Id,
            Link("a", "drop me"),
            Link("b", "drop me too"),
            Link("c", "keep one"),
            Link("d", "keep two"),
            Link("e", "keep three"));

        var run = await LastRunAsync(client, source.Id);

        // Filter first, cap second. The other order would have spent the cap on the two rejects
        // and brought in nothing at all.
        Assert.Equal(2, run.FoundCount);
        // Two rejected by the pattern, one more left behind by the cap.
        Assert.Equal(3, run.SkippedCount);
    }

    [Fact]
    public async Task Minimum_age_holds_something_back_and_lets_it_through_later()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client, new { minAgeHours = 24 });

        var fresh = Link("fresh", "Just posted", DateTimeOffset.UtcNow.AddMinutes(-30));
        await RunAsync(app, source.Id, fresh);
        Assert.Equal(0, (await LastRunAsync(client, source.Id)).FoundCount);

        // The same link, once it is old enough. Nothing was lost by waiting.
        await RunAsync(app, source.Id, Link("fresh", "Just posted", DateTimeOffset.UtcNow.AddDays(-2)));
        Assert.Equal(1, (await LastRunAsync(client, source.Id)).FoundCount);
    }

    [Fact]
    public async Task Without_a_window_a_deleted_item_comes_straight_back()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var playlist = await NewPlaylistAsync(client);
        var source = await NewSourceAsync(client, filter: null, [playlist.Id]);

        await RunAsync(app, source.Id, Link("again", "Comes back"));
        await DeleteAsync(client, (await ItemIdsAsync(playlist.Id))[0]);

        await RunAsync(app, source.Id, Link("again", "Comes back"));

        Assert.Single(await ItemIdsAsync(playlist.Id));
    }

    [Fact]
    public async Task A_dedupe_window_makes_a_deletion_stick()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var playlist = await NewPlaylistAsync(client);
        var source = await NewSourceAsync(client, new { dedupeWindowDays = 7 }, [playlist.Id]);

        await RunAsync(app, source.Id, Link("no-thanks", "Not for me"));
        await DeleteAsync(client, (await ItemIdsAsync(playlist.Id))[0]);

        await RunAsync(app, source.Id, Link("no-thanks", "Not for me"));

        Assert.Empty(await ItemIdsAsync(playlist.Id));
    }

    [Fact]
    public async Task Past_the_window_the_source_is_free_to_bring_it_back()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var playlist = await NewPlaylistAsync(client);
        var source = await NewSourceAsync(client, new { dedupeWindowDays = 1 }, [playlist.Id]);

        await RunAsync(app, source.Id, Link("old-news", "Old news"));
        var itemId = (await ItemIdsAsync(playlist.Id))[0];
        await DeleteAsync(client, itemId);

        await BackdateDeletionAsync(itemId, DateTimeOffset.UtcNow.AddDays(-3));
        await RunAsync(app, source.Id, Link("old-news", "Old news"));

        Assert.Single(await ItemIdsAsync(playlist.Id));
    }

    [Fact]
    public async Task Clearing_a_filter_takes_an_empty_one_rather_than_a_null()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client, new { titleExclude = "sponsored" });

        // Omitted means "leave it alone" — that is what every other field on the request means.
        var untouched = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { name = "Renamed" });
        untouched.EnsureSuccessStatusCode();
        Assert.Equal("sponsored", (await untouched.Content.ReadFromJsonAsync<SourceDto>())!.Filter?.TitleExclude);

        var cleared = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { filter = new { } });
        cleared.EnsureSuccessStatusCode();
        Assert.Null((await cleared.Content.ReadFromJsonAsync<SourceDto>())!.Filter);
    }

    /// <summary>
    /// The playlist's live items, read straight from the rows. The API lists an item only once
    /// enrichment has finished with it, and what a source run leaves behind is pending by
    /// definition — so asking the endpoint would report an empty playlist either way.
    /// </summary>
    private async Task<List<Guid>> ItemIdsAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .OrderBy(i => i.Position)
            .Select(i => i.Id)
            .ToListAsync();
    }

    private static async Task DeleteAsync(HttpClient client, Guid itemId) =>
        (await client.DeleteAsync($"/api/v1/items/{itemId}")).EnsureSuccessStatusCode();

    /// <summary>Ages a removal, so the window can be tested without waiting a day for it.</summary>
    private async Task BackdateDeletionAsync(Guid itemId, DateTimeOffset when)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var item = await db.PlaylistItems.IgnoreQueryFilters().FirstAsync(i => i.Id == itemId);
        item.DeletionTime = when;
        await db.SaveChangesAsync();
    }
}
