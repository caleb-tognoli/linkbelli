using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What a crawler is told about.
/// </summary>
/// <remarks>
/// The sitemap used to be rendered by walking the discovery listing: fifty serial round trips per
/// fetch, each an increasingly deep offset query carrying five correlated subqueries per row — an
/// item count, a tag array, an NSFW check, a like count, a last-activity — none of which a crawler
/// reads. This endpoint answers the question actually being asked: an address and a date.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class SitemapTests(PostgresApiFactory factory)
{
    private record EntryDto(string OwnerUsername, string Slug, DateTimeOffset LastModified);

    private record SitemapPage(List<EntryDto> Items, string? NextCursor);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));
        return (client, username);
    }

    private static async Task<(Guid Id, string Slug)> NewPlaylistAsync(
        HttpClient client, string name, string visibility)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        var created = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        return (created.Id, created.Slug);
    }

    /// <summary>Anonymous: this is read by crawlers, which never sign in.</summary>
    private async Task<SitemapPage> FetchAsync(string query = "")
    {
        var res = await factory.CreateClient().GetAsync($"/api/v1/public/sitemap{query}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SitemapPage>())!;
    }

    [Fact]
    public async Task A_public_playlist_is_listed_with_its_owner_and_slug()
    {
        var (client, username) = await NewUserAsync();
        var (_, slug) = await NewPlaylistAsync(client, "Crawlable " + Guid.NewGuid().ToString("N")[..8], "Public");

        var page = await FetchAsync();

        var entry = Assert.Single(page.Items, e => e.Slug == slug);
        Assert.Equal(username, entry.OwnerUsername, ignoreCase: true);
    }

    /// <summary>
    /// Unlisted is share-by-link and deliberately unfindable. Handing it to a crawler would undo
    /// the only thing the setting does.
    /// </summary>
    [Fact]
    public async Task Unlisted_and_private_playlists_are_not_handed_to_crawlers()
    {
        var (client, _) = await NewUserAsync();
        var (_, unlisted) = await NewPlaylistAsync(client, "By link only " + Guid.NewGuid().ToString("N")[..8], "Unlisted");
        var (_, secret) = await NewPlaylistAsync(client, "Mine alone " + Guid.NewGuid().ToString("N")[..8], "Private");

        var page = await FetchAsync();

        Assert.DoesNotContain(page.Items, e => e.Slug == unlisted);
        Assert.DoesNotContain(page.Items, e => e.Slug == secret);
    }

    /// <summary>
    /// <c>lastmod</c> means when the page last changed, which for a playlist is when something
    /// was last added to it — not when the list was created, which is what was published before.
    /// </summary>
    [Fact]
    public async Task The_date_is_the_newest_thing_in_the_playlist()
    {
        var (client, _) = await NewUserAsync();
        var (id, slug) = await NewPlaylistAsync(client, "Growing " + Guid.NewGuid().ToString("N")[..8], "Public");

        var before = Assert.Single((await FetchAsync()).Items, e => e.Slug == slug);

        await factory.SeedEnrichedItemsAsync(id, 1);

        var after = Assert.Single((await FetchAsync()).Items, e => e.Slug == slug);

        Assert.True(after.LastModified > before.LastModified);
    }

    [Fact]
    public async Task It_pages_and_the_pages_do_not_overlap()
    {
        var (client, _) = await NewUserAsync();
        var slugs = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var (_, slug) = await NewPlaylistAsync(client, $"Paged {Guid.NewGuid():N}", "Public");
            slugs.Add(slug);
        }

        var seen = new List<string>();
        string? cursor = null;
        do
        {
            var page = await FetchAsync($"?limit=2{(cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}")}");
            seen.AddRange(page.Items.Select(e => e.Slug));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(seen.Count, seen.Distinct().Count());
        foreach (var slug in slugs)
        {
            Assert.Contains(slug, seen);
        }
    }

    /// <summary>
    /// A page size far above the hundred every other listing allows, because a row here is three
    /// short fields rather than five subqueries. One round trip is the whole point.
    /// </summary>
    [Fact]
    public async Task It_will_hand_over_far_more_rows_at_once_than_a_normal_listing()
    {
        var res = await factory.CreateClient().GetAsync("/api/v1/public/sitemap?limit=5000");

        res.EnsureSuccessStatusCode();
    }
}
