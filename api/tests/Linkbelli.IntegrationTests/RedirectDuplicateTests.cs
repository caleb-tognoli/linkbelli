using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The same page reached through a redirect.
/// </summary>
/// <remarks>
/// Saving <c>/wiki/RSS</c> and <c>/wiki/Really_Simple_Syndication</c> — the second 301s to the
/// first — produced two rows with the same title, and the duplicates page said "Nothing saved
/// twice". It groups on host and path, which differ; the enricher followed the redirect and threw
/// the answer away.
///
/// A redirect is the most ordinary way one page arrives under two addresses: a shortener, an
/// <c>m.</c> subdomain, a renamed article slug, <c>?amp=1</c>. That is precisely what the
/// duplicates page advertises it catches.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RedirectDuplicateTests(PostgresApiFactory factory)
{
    private record CopyDto(Guid ItemId, Guid PlaylistId, string PlaylistName, string Url, string? Title);

    private record GroupDto(string Kind, string Key, List<CopyDto> Copies);

    private record ItemDto(Guid Id, LinkDto Link);

    private record LinkDto(Guid Id, string Url);

    private record PagedItems(List<ItemDto> Items);

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

    private static async Task<List<GroupDto>> DuplicatesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<GroupDto>>("/api/v1/duplicates"))!;

    /// <summary>The case from the bug report, with unique hosts so the shared database stays quiet.</summary>
    [Fact]
    public async Task Two_addresses_that_lead_to_one_page_are_reported_together()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Wiki");
        var tag = Guid.NewGuid().ToString("N")[..10];

        var longWay = $"https://{tag}.example/wiki/Really_Simple_Syndication";
        var shortWay = $"https://{tag}.example/wiki/RSS";

        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => longWay);
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => shortWay);

        // What the enricher now writes down after following the 301.
        await factory.ResolveLinkAsync(longWay, shortWay);

        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key == shortWay);

        Assert.Equal("SameAfterRedirect", group.Kind);
        Assert.Equal(2, group.Copies.Count);
        Assert.Contains(group.Copies, c => c.Url == longWay);
        Assert.Contains(group.Copies, c => c.Url == shortWay);
    }

    /// <summary>
    /// A shortener shares neither host nor path with what it opens, so the grouping that existed
    /// could never have caught it however many were saved.
    /// </summary>
    [Fact]
    public async Task Two_shorteners_pointing_at_one_article_are_reported_together()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Shortened");
        var tag = Guid.NewGuid().ToString("N")[..10];

        var article = $"https://{tag}.example/the-real-article";
        var first = $"https://s1{tag}.example/abc";
        var second = $"https://s2{tag}.example/xyz";

        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => first);
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => second);
        await factory.ResolveLinkAsync(first, article);
        await factory.ResolveLinkAsync(second, article);

        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key == article);

        Assert.Equal("SameAfterRedirect", group.Kind);
        Assert.Equal(2, group.Copies.Count);
    }

    /// <summary>
    /// One link is not a duplicate of anything, however interesting its redirect is.
    /// </summary>
    [Fact]
    public async Task A_single_redirected_link_is_not_a_duplicate()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Alone");
        var tag = Guid.NewGuid().ToString("N")[..10];

        var saved = $"https://s{tag}.example/abc";
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => saved);
        await factory.ResolveLinkAsync(saved, $"https://{tag}.example/article");

        Assert.DoesNotContain(await DuplicatesAsync(client), g => g.Kind == "SameAfterRedirect");
    }

    /// <summary>
    /// The identical link in two playlists is already reported, and better. It must not also
    /// appear as a redirect group.
    /// </summary>
    [Fact]
    public async Task A_link_already_grouped_is_not_reported_twice()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");
        var tag = Guid.NewGuid().ToString("N")[..10];

        var saved = $"https://s{tag}.example/abc";
        await factory.SeedEnrichedItemsAsync(reading, 1, url: _ => saved);
        await factory.SeedEnrichedItemsAsync(watching, 1, url: _ => saved);
        await factory.ResolveLinkAsync(saved, $"https://{tag}.example/article");

        var mine = (await DuplicatesAsync(client))
            .Where(g => g.Copies.Any(c => c.Url == saved))
            .ToList();

        var group = Assert.Single(mine);
        Assert.Equal("SameLink", group.Kind);
    }

    /// <summary>
    /// Saving the address a page actually lives at keeps that address.
    /// </summary>
    /// <remarks>
    /// The tempting shortcut is to hand back the row somebody already saved under a shortener,
    /// on the grounds that it lands here. That would show the shortener as the address of a page
    /// you saved directly — worse even when it is the same page, and simply wrong when it is
    /// not: a paywall stub, a consent screen and a "this has moved" page all have several
    /// articles redirecting to them.
    ///
    /// So both rows stay, and the pair is offered on the duplicates page as something to look
    /// at. A suggestion can be declined.
    /// </remarks>
    [Fact]
    public async Task Saving_the_address_a_known_link_redirects_to_keeps_that_address()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Both kept");
        var tag = Guid.NewGuid().ToString("N")[..10];

        var shortWay = $"https://s{tag}.example/abc";
        var article = $"https://{tag}.example/the-real-article";

        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => shortWay);
        await factory.ResolveLinkAsync(shortWay, article);
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => article);

        var items = (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!;
        Assert.Equal(2, items.Items.Count);
        Assert.Contains(items.Items, i => i.Link.Url == article);

        // And surfaced, which is the whole point of recording the redirect.
        var group = Assert.Single(await DuplicatesAsync(client), g => g.Key == article);
        Assert.Equal("SameAfterRedirect", group.Kind);
    }
}
