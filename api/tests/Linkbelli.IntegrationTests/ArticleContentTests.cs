using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A saved article nobody can read back is only a saved address, and the copy on the web is the
/// part that rots. These cover reading the stored text, who may read it, and finding a link by a
/// word that only ever appeared in the middle of it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArticleContentTests(PostgresApiFactory factory)
{
    private record ContentDto(
        Guid Id, string Url, string Host, string? Title, string? SiteName,
        string[] Paragraphs, int WordCount, bool Truncated);

    private record ItemDto(Guid Id, LinkDto Link);

    private record LinkDto(Guid Id, string Url, string? Title);

    private record PagedItems(List<ItemDto> Items);

    private record SearchHitDto(Guid ItemId, LinkDto Link, string? Snippet);

    private record SearchPage(List<SearchHitDto> Items, int Total);

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

    /// <summary>Stores article text on a seeded link, the way enrichment would have.</summary>
    private async Task SetContentAsync(Guid linkId, string content, bool truncated = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var link = await db.Links.FirstAsync(l => l.Id == linkId);
        link.Content = new LinkContent { Id = link.Id, Text = content };
        link.WordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        link.ContentTruncated = truncated;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> LinkIdAsync(HttpClient client, Guid playlistId)
    {
        var page = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items");
        return page!.Items[0].Link.Id;
    }

    [Fact]
    public async Task The_stored_article_comes_back_as_paragraphs()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await LinkIdAsync(client, playlist);

        await SetContentAsync(linkId, "The headline\n\nFirst paragraph.\n\nSecond paragraph.");

        var content = await client.GetFromJsonAsync<ContentDto>($"/api/v1/links/{linkId}/content");

        Assert.Equal(3, content!.Paragraphs.Length);
        Assert.Equal("The headline", content.Paragraphs[0]);
        Assert.Equal("Second paragraph.", content.Paragraphs[2]);
        Assert.False(content.Truncated);
    }

    [Fact]
    public async Task A_page_with_no_article_in_it_has_nothing_to_read()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Not an article");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await LinkIdAsync(client, playlist);

        var res = await client.GetAsync($"/api/v1/links/{linkId}/content");

        // Most saved pages are not articles. Saying so is better than an empty reader view.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Only_someone_who_saved_the_page_can_read_it()
    {
        var owner = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Private reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await LinkIdAsync(owner, playlist);
        await SetContentAsync(linkId, string.Join("\n\n", Enumerable.Repeat("A paragraph of the article.", 5)));

        var stranger = await NewUserAsync();
        var res = await stranger.GetAsync($"/api/v1/links/{linkId}/content");

        // Links are global rows shared by everyone who saved the same address; the text behind
        // one is not, or saving a page would hand you every other reader's library.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        (await owner.GetAsync($"/api/v1/links/{linkId}/content")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Truncation_is_reported_rather_than_hidden()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Long read");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await LinkIdAsync(client, playlist);

        await SetContentAsync(linkId, "Only the first part of it.", truncated: true);

        var content = await client.GetFromJsonAsync<ContentDto>($"/api/v1/links/{linkId}/content");

        Assert.True(content!.Truncated);
    }

    [Fact]
    public async Task A_word_from_the_middle_of_an_article_finds_it()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Searchable");
        var word = "zeeuwsevlaamse";
        await factory.SeedEnrichedItemsAsync(playlist, 2);
        var page = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        await SetContentAsync(page!.Items[0].Link.Id, $"Nothing in the title says so, but the {word} line closed in 1952.");

        var results = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={word}");

        Assert.Equal(1, results!.Total);
        Assert.Equal(page.Items[0].Link.Id, results.Items[0].Link.Id);
    }

    [Fact]
    public async Task A_content_only_hit_shows_where_it_matched()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Snippets");
        var word = "narrowgauge";
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await LinkIdAsync(client, playlist);
        await SetContentAsync(linkId, $"An opening line. The {word} section ran along the coast for thirty years.");

        var results = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={word}");

        // Without this the hit looks like a mistake: nothing on the row contains the word typed.
        //
        // Contains rather than StartsWith. The snippet used to be cut at the first literal
        // occurrence, so it necessarily began with the word; ts_headline centres the window on
        // the match and keeps the words on either side, which is the point of a snippet. What
        // has to be true is that the reader can see why this row is here.
        var snippet = results!.Items[0].Snippet;
        Assert.NotNull(snippet);
        Assert.Contains(word, snippet);
        Assert.Contains("along the coast", snippet);
    }

    [Fact]
    public async Task A_title_hit_needs_no_snippet()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Titles");
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: _ => "Trams of Rotterdam");
        var linkId = await LinkIdAsync(client, playlist);
        await SetContentAsync(linkId, "The trams of Rotterdam have run since 1879 and still do.");

        var results = await client.GetFromJsonAsync<SearchPage>("/api/v1/search?q=trams");

        // The reason it matched is already on the row; quoting the article underneath is noise.
        Assert.Null(results!.Items[0].Snippet);
    }
}
