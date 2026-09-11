using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Search across every playlist the caller owns — "where did I put it", rather than "where in
/// this list is it". The isolation guarantee matters most: it must never cross an account.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class GlobalSearchTests(PostgresApiFactory factory)
{
    private record HitLinkDto(Guid Id, string Url, string Host, string? Title);
    private record HitDto(Guid ItemId, Guid PlaylistId, string PlaylistName, HitLinkDto Link, string? Note, string Status, int? Score);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);
    private record HostFacetDto(string Hostname, int ItemCount);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name, params string[] tags)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, tags });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<SearchPageDto> SearchAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/api/v1/search?{query}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;
    }

    [Fact]
    public async Task Finds_links_across_every_playlist_and_names_where_each_lives()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        await factory.SeedEnrichedItemsAsync(reading, 1, title: _ => "Kubernetes networking");
        await factory.SeedEnrichedItemsAsync(watching, 1, title: _ => "Kubernetes at scale (talk)");
        await factory.SeedEnrichedItemsAsync(reading, 1, title: _ => "Something else entirely");

        var page = await SearchAsync(client, "q=kubernetes");

        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, h => h.PlaylistName == "Reading");
        Assert.Contains(page.Items, h => h.PlaylistName == "Watching");
    }

    [Fact]
    public async Task Search_never_crosses_into_another_account()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");

        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(mine, 1, title: _ => $"{marker} mine");
        await factory.SeedEnrichedItemsAsync(theirs, 1, title: _ => $"{marker} theirs");

        var ownerResults = await SearchAsync(owner, $"q={marker}");
        var strangerResults = await SearchAsync(stranger, $"q={marker}");

        Assert.Equal(1, ownerResults.Total);
        Assert.EndsWith("mine", ownerResults.Items[0].Link.Title);

        Assert.Equal(1, strangerResults.Total);
        Assert.EndsWith("theirs", strangerResults.Items[0].Link.Title);
    }

    [Fact]
    public async Task A_note_the_caller_wrote_is_searchable()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Notes");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => "Opaque title");

        var marker = "remindme" + Guid.NewGuid().ToString("N")[..8];
        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = $"{marker} before the talk" }))
            .EnsureSuccessStatusCode();

        var page = await SearchAsync(client, $"q={marker}");

        var hit = Assert.Single(page.Items);
        Assert.Contains(marker, hit.Note);
    }

    [Fact]
    public async Task Results_can_be_narrowed_by_host()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Hosts");
        var tag = Guid.NewGuid().ToString("N")[..8];

        await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"Seeded {n}");
        await factory.SeedEnrichedItemsAsync(playlist, 1,
            title: _ => "Elsewhere", url: _ => $"https://other-{tag}.example/a");

        var page = await SearchAsync(client, $"host=other-{tag}.example");

        var hit = Assert.Single(page.Items);
        Assert.Equal("Elsewhere", hit.Link.Title);
    }

    [Fact]
    public async Task Results_can_be_narrowed_by_playlist_tag()
    {
        var client = await NewUserAsync();
        var marker = "tagged" + Guid.NewGuid().ToString("N")[..8];
        var tagged = await NewPlaylistAsync(client, "Tagged", marker);
        var untagged = await NewPlaylistAsync(client, "Untagged");

        await factory.SeedEnrichedItemsAsync(tagged, 1, title: _ => "In the tagged list");
        await factory.SeedEnrichedItemsAsync(untagged, 1, title: _ => "In the untagged list");

        var page = await SearchAsync(client, $"tag={marker}");

        var hit = Assert.Single(page.Items);
        Assert.Equal("In the tagged list", hit.Link.Title);
    }

    [Fact]
    public async Task Results_can_be_narrowed_by_status_and_score()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Filters");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"Filtered {n}");

        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { status = "Watched" }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[1]}/score", new { score = 90 }))
            .EnsureSuccessStatusCode();

        var watched = await SearchAsync(client, "status=watched");
        Assert.Single(watched.Items);
        Assert.Equal(seeded[0], watched.Items[0].ItemId);

        var highlyRated = await SearchAsync(client, "minScore=80");
        Assert.Single(highlyRated.Items);
        Assert.Equal(seeded[1], highlyRated.Items[0].ItemId);
    }

    [Fact]
    public async Task A_bare_search_browses_newest_first()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Browse");
        await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"Browsed {n}");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var items = await db.PlaylistItems.Where(i => i.PlaylistId == playlist)
                .OrderBy(i => i.Position).ToListAsync();
            for (var n = 0; n < items.Count; n++)
            {
                items[n].CreationTime = DateTimeOffset.UtcNow.AddMinutes(-10 + n);
            }
            await db.SaveChangesAsync();
        }

        var page = await SearchAsync(client, "limit=3");

        Assert.Equal("Browsed 2", page.Items[0].Link.Title);
    }

    [Fact]
    public async Task Results_page_without_repeating_or_dropping_hits()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Paged search");
        var marker = "paged" + Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 7, title: n => $"{marker} {n}");

        var seen = new HashSet<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await SearchAsync(client,
                $"q={marker}&limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));

            Assert.Equal(7, page.Total);
            foreach (var hit in page.Items) Assert.True(seen.Add(hit.ItemId), "A hit appeared on two pages.");

            cursor = page.NextCursor;
            Assert.True(++pages <= 5, "Paging did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public async Task Host_facets_count_the_callers_own_links_only()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Facet mine");
        var theirs = await NewPlaylistAsync(stranger, "Facet theirs");

        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(mine, 2, url: n => $"https://facet-{tag}.example/{n}");
        await factory.SeedEnrichedItemsAsync(theirs, 5, url: n => $"https://facet-{tag}.example/theirs-{n}");

        var res = await owner.GetAsync($"/api/v1/search/hosts?q=facet-{tag}");
        res.EnsureSuccessStatusCode();
        var facets = (await res.Content.ReadFromJsonAsync<List<HostFacetDto>>())!;

        var facet = Assert.Single(facets);
        Assert.Equal($"facet-{tag}.example", facet.Hostname);
        Assert.Equal(2, facet.ItemCount);
    }
}
