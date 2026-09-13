using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Typing a filter that was only ever clickable.
/// </summary>
/// <remarks>
/// The parser itself is covered by unit tests. These check the other half: that what it produces
/// actually reaches the query, so <c>site:</c> narrows the results rather than being parsed and
/// dropped on the floor.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class SearchOperatorSearchTests(PostgresApiFactory factory)
{
    private record HitLinkDto(Guid Id, string Url, string Host, string? Title);

    private record HitDto(Guid ItemId, HitLinkDto Link, string Status);

    private record SearchPageDto(List<HitDto> Items, int? Total);

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

    private static async Task<SearchPageDto> SearchAsync(HttpClient client, string q) =>
        (await client.GetFromJsonAsync<SearchPageDto>($"/api/v1/search?q={Uri.EscapeDataString(q)}"))!;

    [Fact]
    public async Task Typing_a_site_narrows_to_that_site()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mixed");
        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        var wanted = $"w{Guid.NewGuid():N}".ToLowerInvariant()[..12] + ".example";
        var other = $"o{Guid.NewGuid():N}".ToLowerInvariant()[..12] + ".example";

        await factory.SeedEnrichedItemsAsync(
            playlist, 1, title: _ => $"{marker} on the one I want", url: _ => $"https://{wanted}/a");
        await factory.SeedEnrichedItemsAsync(
            playlist, 1, title: _ => $"{marker} somewhere else", url: _ => $"https://{other}/b");

        Assert.Equal(2, (await SearchAsync(client, marker)).Total);

        var narrowed = await SearchAsync(client, $"{marker} site:{wanted}");

        var hit = Assert.Single(narrowed.Items);
        Assert.Equal(wanted, hit.Link.Host);
    }

    [Fact]
    public async Task Typing_is_unread_leaves_out_what_is_finished()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Some read");
        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];

        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"{marker} {n}");
        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { status = "Watched" }))
            .EnsureSuccessStatusCode();

        var unread = await SearchAsync(client, $"{marker} is:unread");

        var hit = Assert.Single(unread.Items);
        Assert.Equal(seeded[1], hit.ItemId);
    }

    [Fact]
    public async Task Typing_a_tag_narrows_to_links_carrying_it()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Tagged");
        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        var tag = "topic" + Guid.NewGuid().ToString("N")[..8];

        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"{marker} {n}");
        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { tags = new[] { tag } }))
            .EnsureSuccessStatusCode();

        var tagged = await SearchAsync(client, $"{marker} tag:{tag}");

        Assert.Equal(seeded[0], Assert.Single(tagged.Items).ItemId);
    }

    /// <summary>
    /// A query of nothing but operators has no words left to match, and must still be a search
    /// rather than falling back to everything.
    /// </summary>
    [Fact]
    public async Task Operators_alone_are_a_search()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Only operators");
        var wanted = $"w{Guid.NewGuid():N}".ToLowerInvariant()[..12] + ".example";

        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://{wanted}/a");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var narrowed = await SearchAsync(client, $"site:{wanted}");

        Assert.Equal(wanted, Assert.Single(narrowed.Items).Link.Host);
    }

    /// <summary>
    /// The reason the parser has a tokenizer: a colon inside quotes is part of the phrase, and
    /// must not silently become a filter that matches nothing.
    /// </summary>
    [Fact]
    public async Task An_operator_inside_quotes_is_searched_for_rather_than_applied()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Quoted");
        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];

        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => $"{marker} site reliability engineering");

        // Without the tokenizer this would be read as site:reliability and match nothing.
        var found = await SearchAsync(client, $"{marker} \"site reliability\"");

        Assert.Single(found.Items);
    }
}
