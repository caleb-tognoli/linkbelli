using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The article text rides on the link's row but not on the link.
/// </summary>
/// <remarks>
/// Every place that loaded a link used to drag the whole article with it — the automation sweep
/// alone read two hundred of them a minute. Measured on this app's data: 19 ms for two hundred
/// links with their text, half a millisecond without. The text is now its own entity on the same
/// row, and these pin down the two things that has to mean.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class LinkContentSplitTests(PostgresApiFactory factory)
{
    private record ItemDto(Guid Id, LinkRef Link);

    private record LinkRef(Guid Id);

    private record PagedItems(List<ItemDto> Items);

    private record ContentDto(string[] Paragraphs);

    private async Task<(HttpClient Client, Guid LinkId)> ArticleAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));

        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Split {Guid.NewGuid():N}" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        var linkId = (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items[0].Link.Id;
        return (client, linkId);
    }

    /// <summary>
    /// The query that made this worth doing: the automation sweep, every minute, two hundred
    /// items with their links. Asserted on the SQL itself, so a change that quietly puts the text
    /// back on the link fails here rather than showing up as a slower minute nobody measures.
    /// </summary>
    [Fact]
    public void The_sweeps_query_does_not_read_article_text()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var sql = db.PlaylistItems
            .Include(i => i.Link).ThenInclude(l => l!.Host)
            .Include(i => i.Playlist)
            .Include(i => i.Tags)
            .Where(i => i.AutomationAppliedAt == null)
            .ToQueryString();

        Assert.DoesNotContain("\"Content\"", sql);
    }

    [Fact]
    public async Task Loading_a_link_does_not_load_its_article()
    {
        var (_, linkId) = await ArticleAsync();
        await factory.SetArticleTextAsync(linkId, "A long article nobody asked to read here.");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var plain = await db.Links.AsNoTracking().FirstAsync(l => l.Id == linkId);
        Assert.Null(plain.Content);

        // And it is still there for the one place that asks.
        var asked = await db.Links.AsNoTracking().Include(l => l.Content).FirstAsync(l => l.Id == linkId);
        Assert.Equal("A long article nobody asked to read here.", asked.Content?.Text);
    }

    /// <summary>
    /// Enrichment writes the text without loading the old text first. The text shares the row,
    /// so attaching it has to become an update of that row — twice over, when a page is
    /// re-enriched — and never an insert that collides with the link itself.
    /// </summary>
    [Fact]
    public async Task Writing_the_text_again_replaces_it_on_the_same_row()
    {
        var (client, linkId) = await ArticleAsync();

        await factory.SetArticleTextAsync(linkId, "First version.");
        await factory.SetArticleTextAsync(linkId, "Second version.\n\nWith another paragraph.");

        var content = await client.GetFromJsonAsync<ContentDto>($"/api/v1/links/{linkId}/content");
        Assert.Equal(["Second version.", "With another paragraph."], content!.Paragraphs);
    }

    /// <summary>A page whose article went away is back to having none, not left with the old one.</summary>
    [Fact]
    public async Task Clearing_the_text_leaves_nothing_to_read()
    {
        var (client, linkId) = await ArticleAsync();
        await factory.SetArticleTextAsync(linkId, "Here for now.");

        await factory.SetArticleTextAsync(linkId, null);

        var res = await client.GetAsync($"/api/v1/links/{linkId}/content");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    /// The search vector is generated from this column on this row, which is the reason the text
    /// stayed on the row. Writing it through the new entity still has to reach the index.
    /// </summary>
    [Fact]
    public async Task Text_written_through_the_new_entity_is_still_searchable()
    {
        var (client, linkId) = await ArticleAsync();
        var word = "zq" + Guid.NewGuid().ToString("N")[..10];

        await factory.SetArticleTextAsync(linkId, $"Somewhere in the middle it says {word} once.");

        var hits = await client.GetFromJsonAsync<PagedItems>($"/api/v1/search?q={word}");
        Assert.Equal(linkId, Assert.Single(hits!.Items).Link.Id);
    }
}
