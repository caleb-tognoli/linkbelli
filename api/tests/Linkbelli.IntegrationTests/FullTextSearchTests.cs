using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What the search box understands, now that it asks an index rather than scanning every article.
/// </summary>
/// <remarks>
/// Matching used to be <c>LOWER(col) LIKE '%needle%'</c> across seven columns, one holding up to
/// 60 000 characters of article text. Moving to a stored tsvector makes it fast, and also changes
/// what it can answer: stemming, quoted phrases and exclusions all arrive with it. These pin the
/// behaviour that came for free alongside the behaviour that had to keep working.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class FullTextSearchTests(PostgresApiFactory factory)
{
    private record LinkDto(Guid Id, string Url, string? Title);

    private record ItemDto(Guid Id, LinkDto Link);

    private record PagedItems(List<ItemDto> Items);

    private record SearchHitDto(Guid ItemId, LinkDto Link, string? Snippet);

    private record SearchPage(List<SearchHitDto> Items, int Total);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private async Task<(HttpClient Client, Guid LinkId)> WithArticleAsync(string content, string? title = null)
    {
        var client = await NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"S {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        await factory.SeedEnrichedItemsAsync(playlist, 1, title: title is null ? null : _ => title);
        var page = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        var linkId = page!.Items[0].Link.Id;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var link = await db.Links.FirstAsync(l => l.Id == linkId);
        link.Content = new LinkContent { Id = link.Id, Text = content };
        link.WordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        await db.SaveChangesAsync();

        return (client, linkId);
    }

    private static async Task<SearchPage> SearchAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={Uri.EscapeDataString(query)}"))!;

    /// <summary>
    /// The thing the old substring match could not do, and the reason people think search is
    /// broken: you type the plural and the article uses the singular.
    /// </summary>
    [Theory]
    [InlineData("ferries")]
    [InlineData("ferry")]
    [InlineData("FERRIES")]
    public async Task Finds_a_word_in_whatever_form_it_was_typed(string query)
    {
        var (client, linkId) = await WithArticleAsync("The ferry to Vlissingen ran until 2003.");

        var results = await SearchAsync(client, query);

        Assert.Equal(1, results.Total);
        Assert.Equal(linkId, results.Items[0].Link.Id);
    }

    [Fact]
    public async Task A_quoted_phrase_wants_the_words_in_that_order()
    {
        var (client, linkId) = await WithArticleAsync("A narrow gauge railway, and separately a standard gauge line.");

        var together = await SearchAsync(client, "\"narrow gauge\"");
        var apart = await SearchAsync(client, "\"gauge narrow\"");

        Assert.Equal(linkId, together.Items[0].Link.Id);
        Assert.Equal(0, apart.Total);
    }

    [Fact]
    public async Task A_minus_sign_takes_something_out()
    {
        var (client, _) = await WithArticleAsync("The tram and the ferry both stopped in 2003.");

        Assert.Equal(1, (await SearchAsync(client, "tram")).Total);
        Assert.Equal(0, (await SearchAsync(client, "tram -ferry")).Total);
    }

    /// <summary>
    /// Somebody searching for "100%" means the character. As a LIKE pattern it is a wildcard, and
    /// the literal columns are still matched with LIKE.
    /// </summary>
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("%%")]
    public async Task Wildcard_characters_are_taken_literally(string query)
    {
        var (client, _) = await WithArticleAsync("An article with no unusual punctuation in it.");

        var results = await SearchAsync(client, query);

        // The point is that it does not match everything the caller owns. Zero is the honest
        // answer for a document that contains no percent sign.
        Assert.Equal(0, results.Total);
    }

    [Theory]
    [InlineData(":::")]
    [InlineData("&&&")]
    [InlineData("!")]
    [InlineData("   ")]
    [InlineData("\"unclosed")]
    public async Task Punctuation_on_its_own_is_answered_rather_than_thrown(string query)
    {
        var (client, _) = await WithArticleAsync("Something ordinary.");

        // A search box is not a parser. Whatever somebody types, this answers.
        var res = await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(query)}");

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_pasted_address_still_finds_exactly_that_link()
    {
        var client = await NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"U {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        await factory.SeedEnrichedItemsAsync(playlist, 3);
        var page = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        var target = page!.Items[1].Link;

        var results = await SearchAsync(client, target.Url);

        // Canonicalised and matched on the dedup hash, ahead of the text index — a URL put
        // through a stemmer is nonsense.
        Assert.Equal(1, results.Total);
        Assert.Equal(target.Id, results.Items[0].Link.Id);
    }

    [Fact]
    public async Task A_hostname_still_matches_even_though_it_is_not_english()
    {
        var client = await NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"H {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        var host = $"h{Guid.NewGuid():N}"[..10] + ".example";
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: n => $"https://{host}/page/{n}");

        var results = await SearchAsync(client, host);

        Assert.Equal(1, results.Total);
    }

    [Fact]
    public async Task A_title_match_ranks_above_a_passing_mention_in_the_body()
    {
        var client = await NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"R {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => "Buried mention", url: _ => $"https://a{Guid.NewGuid():N}.example/");
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => "Zeppelins over Friesland", url: _ => $"https://b{Guid.NewGuid():N}.example/");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var buried = await db.Links.FirstAsync(l => l.Title == "Buried mention");
            buried.Content = new LinkContent { Id = buried.Id, Text = "A long piece about canals that mentions zeppelins exactly once, here." };
            await db.SaveChangesAsync();
        }

        var results = await SearchAsync(client, "zeppelins");

        // The weighting in the generated column is what says this, in place of the three
        // substring scans the ORDER BY used to do.
        Assert.Equal(2, results.Total);
        Assert.Equal("Zeppelins over Friesland", results.Items[0].Link.Title);
    }

    [Fact]
    public async Task A_note_is_searchable_even_though_it_is_not_in_the_vector()
    {
        var client = await NewUserAsync();
        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"N {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var page = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");

        var marker = $"note{Guid.NewGuid():N}"[..12];
        await client.PatchAsJsonAsync($"/api/v1/items/{page!.Items[0].Id}", new { note = $"Read this {marker} later" });

        var results = await SearchAsync(client, marker);

        Assert.Equal(1, results.Total);
    }

    [Fact]
    public async Task Search_stays_inside_the_caller_s_own_library()
    {
        var word = $"w{Guid.NewGuid():N}"[..12];
        var (_, _) = await WithArticleAsync($"A private article about {word}.");

        var stranger = await NewUserAsync();
        var results = await SearchAsync(stranger, word);

        Assert.Equal(0, results.Total);
    }
}
