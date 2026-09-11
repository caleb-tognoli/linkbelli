using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Searching within a playlist: what matches, how it is ordered, and that the trigram indexes
/// the search predicate relies on are actually in the database.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemSearchTests(PostgresApiFactory factory)
{
    private record LinkDto(Guid Id, string Url, string Host, string? Title);
    private record ItemWithLinkDto(Guid Id, LinkDto Link);
    private record PagedDto(List<ItemWithLinkDto> Items, string? NextCursor, int? Total);

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

    private static async Task<PagedDto> SearchAsync(HttpClient client, Guid playlistId, string query)
    {
        var res = await client.GetAsync($"/api/v1/playlists/{playlistId}/items?{query}");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PagedDto>())!;
    }

    [Fact]
    public async Task A_title_match_ranks_above_a_description_match()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Ranking");

        // Seeded in an order that puts the weaker match first, so position alone would get it wrong.
        await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => n == 0 ? "Unrelated" : "Postgres tuning");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var weak = await db.PlaylistItems.Include(i => i.Link)
                .Where(i => i.PlaylistId == playlist)
                .OrderBy(i => i.Position).FirstAsync();
            weak.Link!.Description = "A footnote that mentions postgres in passing";
            await db.SaveChangesAsync();
        }

        var page = await SearchAsync(client, playlist, "q=postgres");

        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Postgres tuning", page.Items[0].Link.Title);
    }

    [Fact]
    public async Task An_explicit_sort_wins_over_relevance()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Explicit sort");
        await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"Postgres {n}");

        // Seeding writes all three in one SaveChanges, so they share a creation timestamp to the
        // tick; stagger them so "oldest first" has something real to order by.
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

        // The user asked for oldest-first; a search term must not quietly override that.
        var page = await SearchAsync(client, playlist, "q=postgres&sort=date-asc");

        Assert.Equal(3, page.Items.Count);
        Assert.Equal("Postgres 0", page.Items[0].Link.Title);
    }

    [Fact]
    public async Task Search_matches_across_title_url_and_host()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Fields");
        var tag = Guid.NewGuid().ToString("N")[..8];

        await factory.SeedEnrichedItemsAsync(playlist, 1,
            title: _ => "Nothing notable", url: _ => $"https://seed.example/{tag}/kubernetes-guide");

        // Matched on the URL, not the title.
        Assert.Single((await SearchAsync(client, playlist, "q=kubernetes")).Items);

        // Matched on the hostname.
        Assert.Single((await SearchAsync(client, playlist, "q=seed.example")).Items);
    }

    [Fact]
    public async Task Searching_a_url_matches_that_exact_link()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Url lookup");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 3, url: n => $"https://seed.example/{tag}/{n}");

        // A pasted URL is canonicalized and matched on the indexed dedup hash, not as text —
        // so it finds exactly one item even though all three share a prefix.
        var page = await SearchAsync(client, playlist,
            $"q={Uri.EscapeDataString($"https://seed.example/{tag}/1?utm_source=newsletter")}");

        var item = Assert.Single(page.Items);
        Assert.EndsWith("/1", item.Link.Url);
    }

    [Fact]
    public async Task A_search_that_matches_nothing_returns_an_empty_page_not_everything()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "No matches");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        var page = await SearchAsync(client, playlist, "q=zzzznotpresentzzzz");

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Search_results_page_without_repeating_or_dropping_items()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Search paging");
        await factory.SeedEnrichedItemsAsync(playlist, 7, title: n => $"Postgres note {n}");

        var seen = new HashSet<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await SearchAsync(client, playlist,
                "q=postgres&limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"));

            Assert.Equal(7, page.Total);
            foreach (var item in page.Items) Assert.True(seen.Add(item.Id), "An item appeared on two pages.");

            cursor = page.NextCursor;
            Assert.True(++pages <= 5, "Paging did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(7, seen.Count);
    }

    [Fact]
    public async Task The_trigram_indexes_the_search_predicate_needs_exist()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT indexname FROM pg_indexes
            WHERE indexname LIKE '%_trgm'
            ORDER BY indexname
            """;

        var found = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) found.Add(reader.GetString(0));
        }

        // Without these, lower(col) LIKE '%term%' is a sequential scan on every keystroke.
        Assert.Contains("IX_Links_Title_trgm", found);
        Assert.Contains("IX_Links_Description_trgm", found);
        Assert.Contains("IX_Links_CanonicalUrl_trgm", found);
        Assert.Contains("IX_Hosts_Hostname_trgm", found);
    }
}
