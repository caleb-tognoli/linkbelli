using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Core.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A collection is one undifferentiated list of addresses until something says what each link is.
/// These cover the filters that buys — "show me a video" and "something under five minutes" — and
/// the sweep that classifies links saved before anything was asking.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ContentKindTests(PostgresApiFactory factory)
{
    private record LinkDto(Guid Id, string Url, string Kind, int? WordCount);

    private record ItemDto(Guid Id, LinkDto Link);

    private record PagedItems(List<ItemDto> Items);

    private record SearchHitDto(Guid ItemId, LinkDto Link);

    private record SearchPage(List<SearchHitDto> Items, int Total);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Marks every link that already exists as classified, so a sweep's batch holds only what a
    /// test seeds next.
    /// </summary>
    /// <remarks>
    /// The suite shares one database and the sweep is deliberately owner-agnostic — it exists to
    /// catch up on everything saved before classification existed. That makes "did the sweep
    /// reach my link" unanswerable unless the rest of the field is settled first.
    /// </remarks>
    private async Task SettleEveryOtherLinkAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        await db.Links
            .Where(l => l.Kind == ContentKind.Unknown)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Kind, ContentKind.Article));
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    /// <summary>Stamps a link the way enrichment would have.</summary>
    private async Task StampAsync(Guid linkId, ContentKind? kind = null, int? wordCount = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var link = await db.Links.FirstAsync(l => l.Id == linkId);
        if (kind is { } value) link.Kind = value;
        if (wordCount is { } words)
        {
            link.WordCount = words;
            link.Content = "Stored article text.";
        }

        await db.SaveChangesAsync();
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    [Fact]
    public async Task A_search_can_ask_for_one_kind_of_thing()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mixed");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"{tag} item {n}");
        var items = await ItemsAsync(client, playlist);

        await StampAsync(items[0].Link.Id, ContentKind.Video);
        await StampAsync(items[1].Link.Id, ContentKind.Article);
        await StampAsync(items[2].Link.Id, ContentKind.Article);

        var videos = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={tag}&kind=video");

        Assert.Equal(1, videos!.Total);
        Assert.Equal("Video", videos.Items[0].Link.Kind);
    }

    [Fact]
    public async Task A_kind_nobody_has_heard_of_matches_nothing_rather_than_everything()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Typos");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"{tag} item {n}");

        var results = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={tag}&kind=vidoe");

        // A typo that quietly returns the whole collection looks like the filter is broken.
        Assert.Equal(0, results!.Total);
    }

    [Fact]
    public async Task Something_short_is_a_question_that_can_be_asked()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Lengths");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"{tag} item {n}");
        var items = await ItemsAsync(client, playlist);

        await StampAsync(items[0].Link.Id, ContentKind.Article, wordCount: 400);    // ~2 min
        await StampAsync(items[1].Link.Id, ContentKind.Article, wordCount: 12_000); // ~55 min
        // The third has no article behind it at all.

        var quick = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={tag}&maxMinutes=5");

        // Only the short one: the long read is out, and so is the link with no length to compare.
        Assert.Equal(1, quick!.Total);
        Assert.Equal(400, quick.Items[0].Link.WordCount);
    }

    [Fact]
    public async Task The_boundary_matches_what_the_row_says()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Boundaries");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => $"{tag} five minutes");
        var items = await ItemsAsync(client, playlist);

        // 1,100 words displays as "5 min", so asking for five minutes has to include it — a
        // filter that disagrees with the number on the row is worse than no filter.
        await StampAsync(items[0].Link.Id, ContentKind.Article, wordCount: 1_100);

        var quick = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={tag}&maxMinutes=5");

        Assert.Equal(1, quick!.Total);
    }

    [Fact]
    public async Task The_sweep_classifies_links_saved_before_anything_asked()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Old saves");

        // Settled first, then seeded. The sweep takes the oldest 500 unclassified links belonging
        // to anybody, so once the suite holds more than that this test's two — the newest in the
        // database — are never in the batch, and it fails having tested nothing.
        await SettleEveryOtherLinkAsync();

        await factory.SeedEnrichedItemsAsync(
            playlist, 2, url: n => n == 0 ? "https://www.youtube.com/watch?v=swept" : "https://github.com/swept/repo");

        var items = await ItemsAsync(client, playlist);
        Assert.All(items, item => Assert.Equal("Unknown", item.Link.Kind));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILinkClassificationSweep>().SweepAsync();
        }

        var after = await ItemsAsync(client, playlist);
        Assert.Contains(after, item => item.Link.Kind == "Video");
        Assert.Contains(after, item => item.Link.Kind == "Repository");
    }

    [Fact]
    public async Task The_sweep_leaves_alone_what_it_already_settled()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Already known");
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://example.com/{Guid.NewGuid():N}");
        var linkId = (await ItemsAsync(client, playlist))[0].Link.Id;

        // A hand-set kind the address alone would never produce.
        await StampAsync(linkId, ContentKind.Paper);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILinkClassificationSweep>().SweepAsync();
        }

        Assert.Equal("Paper", (await ItemsAsync(client, playlist))[0].Link.Kind);
    }
}
