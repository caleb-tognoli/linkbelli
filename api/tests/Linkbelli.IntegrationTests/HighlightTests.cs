using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Marking passages in a saved article.
/// </summary>
/// <remarks>
/// The app extracted and kept the readable text of every article, then rendered it as inert
/// paragraphs. Highlighting is the most-used feature of every read-later tool, and the thing that
/// makes the stored copy worth more than the link.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class HighlightTests(PostgresApiFactory factory)
{
    private const string Article =
        "The headline\n\nThe first paragraph says something worth keeping.\n\nThe second one does not.";

    private record HighlightDto(
        Guid Id, Guid LinkId, int ParagraphIndex, int Start, int End, string Text, string? Note, bool Orphaned);

    private record WithSourceDto(Guid Id, Guid LinkId, string Url, string? Title, string Text, string? Note);

    private record PagedHighlights(List<WithSourceDto> Items, string? NextCursor);

    private record ItemDto(Guid Id, LinkRef Link);

    private record LinkRef(Guid Id);

    private record PagedItems(List<ItemDto> Items);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    /// <summary>A playlist holding one link with <see cref="Article"/> stored as its text.</summary>
    private async Task<Guid> ArticleAsync(HttpClient client, string content = Article)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Reading {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!
            .Items[0].Link.Id;

        await SetContentAsync(linkId, content);
        return linkId;
    }

    private async Task SetContentAsync(Guid linkId, string content)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var link = await db.Links.FirstAsync(l => l.Id == linkId);
        link.Content = content;
        link.WordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> MarkAsync(
        HttpClient client, Guid linkId, int paragraph, int start, int end, string? note = null) =>
        client.PostAsJsonAsync($"/api/v1/links/{linkId}/highlights", new
        {
            paragraphIndex = paragraph,
            start,
            end,
            text = "",
            note,
        });

    private static async Task<HighlightDto> MarkedAsync(
        HttpClient client, Guid linkId, int paragraph, int start, int end, string? note = null)
    {
        var res = await MarkAsync(client, linkId, paragraph, start, end, note);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<HighlightDto>())!;
    }

    private static async Task<List<HighlightDto>> ListAsync(HttpClient client, Guid linkId) =>
        (await client.GetFromJsonAsync<List<HighlightDto>>($"/api/v1/links/{linkId}/highlights"))!;

    [Fact]
    public async Task A_passage_can_be_marked_and_comes_back_with_the_article()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);

        // "something worth keeping" in the first paragraph.
        var created = await MarkedAsync(client, linkId, 1, 25, 48);

        Assert.Equal("something worth keeping", created.Text);
        Assert.False(created.Orphaned);

        var listed = Assert.Single(await ListAsync(client, linkId));
        Assert.Equal(created.Id, listed.Id);
    }

    /// <summary>
    /// The quote is taken from the stored article, not from the request — otherwise a client
    /// could file any words it liked under a passage it pointed at.
    /// </summary>
    [Fact]
    public async Task The_words_are_the_ones_in_the_article_not_the_ones_sent()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);

        var res = await client.PostAsJsonAsync($"/api/v1/links/{linkId}/highlights", new
        {
            paragraphIndex = 1,
            start = 0,
            end = 3,
            text = "something else entirely",
        });
        res.EnsureSuccessStatusCode();

        Assert.Equal("The", (await res.Content.ReadFromJsonAsync<HighlightDto>())!.Text);
    }

    [Fact]
    public async Task A_note_can_be_added_changed_and_cleared()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        var created = await MarkedAsync(client, linkId, 1, 0, 3);

        var res = await client.PatchAsJsonAsync($"/api/v1/highlights/{created.Id}", new { note = "  Why this matters  " });
        res.EnsureSuccessStatusCode();
        Assert.Equal("Why this matters", (await res.Content.ReadFromJsonAsync<HighlightDto>())!.Note);

        res = await client.PatchAsJsonAsync($"/api/v1/highlights/{created.Id}", new { note = "   " });
        res.EnsureSuccessStatusCode();
        Assert.Null((await res.Content.ReadFromJsonAsync<HighlightDto>())!.Note);
    }

    [Fact]
    public async Task Marking_the_same_passage_twice_is_one_highlight()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);

        var first = await MarkedAsync(client, linkId, 1, 0, 3);
        var second = await MarkedAsync(client, linkId, 1, 0, 3);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await ListAsync(client, linkId));
    }

    [Fact]
    public async Task Removing_one_takes_it_out_of_the_article()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        var created = await MarkedAsync(client, linkId, 1, 0, 3);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/highlights/{created.Id}")).StatusCode);

        Assert.Empty(await ListAsync(client, linkId));
    }

    [Theory]
    [InlineData(7, 0, 3)] // no such paragraph
    [InlineData(-1, 0, 3)]
    [InlineData(1, 10, 5)] // backwards
    [InlineData(1, 4, 4)] // empty
    [InlineData(1, 0, 500)] // past the end of the paragraph
    public async Task A_selection_that_is_not_in_the_article_is_refused(int paragraph, int start, int end)
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);

        var res = await MarkAsync(client, linkId, paragraph, start, end);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// Links are shared rows. Somebody who never saved this one has no business reading its
    /// text, and marking it is reading it.
    /// </summary>
    [Fact]
    public async Task An_article_you_have_not_saved_cannot_be_marked()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();
        var linkId = await ArticleAsync(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await MarkAsync(stranger, linkId, 1, 0, 3)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/links/{linkId}/highlights")).StatusCode);
    }

    [Fact]
    public async Task Somebody_elses_highlight_is_not_yours_to_change()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();
        var linkId = await ArticleAsync(owner);
        var created = await MarkedAsync(owner, linkId, 1, 0, 3);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.PatchAsJsonAsync($"/api/v1/highlights/{created.Id}", new { note = "mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/highlights/{created.Id}")).StatusCode);

        Assert.Null(Assert.Single(await ListAsync(owner, linkId)).Note);
    }

    /// <summary>
    /// Two people who saved the same link each see their own marks, and only their own. The link
    /// row is shared; what somebody chose in it is not.
    /// </summary>
    [Fact]
    public async Task Marks_are_private_to_whoever_made_them()
    {
        var first = await NewUserAsync();
        var linkId = await ArticleAsync(first);
        await MarkedAsync(first, linkId, 1, 0, 3);

        var second = await NewUserAsync();
        var res = await second.PostAsJsonAsync("/api/v1/playlists", new { name = $"Also reading {Guid.NewGuid():N}" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        var url = await UrlOfAsync(linkId);
        (await second.PostAsJsonAsync($"/api/v1/playlists/{playlist}/items", new { url })).EnsureSuccessStatusCode();

        Assert.Empty(await ListAsync(second, linkId));
    }

    /// <summary>
    /// The paragraphs can shift under a highlight when an article is enriched again. The quote is
    /// kept for exactly this, so the highlight is still there to read — it just is not drawn over
    /// whatever words now happen to sit at those offsets.
    /// </summary>
    [Fact]
    public async Task A_highlight_whose_words_have_moved_is_kept_and_says_so()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        var created = await MarkedAsync(client, linkId, 1, 25, 48);

        await SetContentAsync(linkId, "A new standfirst\n\n" + Article);

        var listed = Assert.Single(await ListAsync(client, linkId));
        Assert.Equal(created.Id, listed.Id);
        Assert.True(listed.Orphaned);
        Assert.Equal("something worth keeping", listed.Text);
    }

    /// <summary>A link in two playlists is one article, and its marks are the same in both.</summary>
    [Fact]
    public async Task A_link_saved_twice_has_one_set_of_marks()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        await MarkedAsync(client, linkId, 1, 0, 3);

        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Second copy {Guid.NewGuid():N}" });
        var other = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        (await client.PostAsJsonAsync($"/api/v1/playlists/{other}/items", new { url = await UrlOfAsync(linkId) }))
            .EnsureSuccessStatusCode();

        Assert.Single(await ListAsync(client, linkId));
    }

    [Fact]
    public async Task Everything_marked_is_listed_newest_first_with_where_it_came_from()
    {
        var client = await NewUserAsync();
        var first = await ArticleAsync(client);
        var second = await ArticleAsync(client);

        await MarkedAsync(client, first, 1, 0, 3);
        await MarkedAsync(client, second, 2, 0, 3, note: "The later one");

        var page = await client.GetFromJsonAsync<PagedHighlights>("/api/v1/highlights");

        Assert.Equal(2, page!.Items.Count);
        Assert.Equal(second, page.Items[0].LinkId);
        Assert.Equal("The later one", page.Items[0].Note);
        Assert.StartsWith("http", page.Items[0].Url);
        Assert.Equal(first, page.Items[1].LinkId);
    }

    [Fact]
    public async Task The_full_list_pages_without_repeating_anything()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        for (var i = 0; i < 5; i++)
        {
            await MarkedAsync(client, linkId, 1, i, i + 3);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/api/v1/highlights?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await client.GetFromJsonAsync<PagedHighlights>(url))!;
            seen.AddRange(page.Items.Select(h => h.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Distinct().Count());
    }

    private record RestorePlanDto(bool DryRun, int ItemsAdded, int HighlightsAdded);

    private static async Task<RestorePlanDto> RestoreAsync(HttpClient client, string json, bool dryRun)
    {
        var res = await client.PostAsJsonAsync("/api/v1/backups/restore", new { json, dryRun });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RestorePlanDto>())!;
    }

    /// <summary>
    /// A backup that brings back the links and loses what you marked in them has kept the part
    /// that was easiest to find again.
    /// </summary>
    [Fact]
    public async Task Highlights_and_their_notes_survive_a_backup_and_restore()
    {
        var author = await NewUserAsync();
        var linkId = await ArticleAsync(author);
        await MarkedAsync(author, linkId, 1, 25, 48, note: "The line I came back for");

        var json = await author.GetStringAsync("/api/v1/export?format=json");

        var restorer = await NewUserAsync();
        var preview = await RestoreAsync(restorer, json, dryRun: true);
        Assert.Equal(1, preview.HighlightsAdded);

        var done = await RestoreAsync(restorer, json, dryRun: false);
        Assert.Equal(preview.HighlightsAdded, done.HighlightsAdded);

        var restored = Assert.Single(await ListAsync(restorer, linkId));
        Assert.Equal("something worth keeping", restored.Text);
        Assert.Equal("The line I came back for", restored.Note);
        Assert.False(restored.Orphaned);
    }

    [Fact]
    public async Task Restoring_twice_does_not_mark_anything_twice()
    {
        var author = await NewUserAsync();
        var linkId = await ArticleAsync(author);
        await MarkedAsync(author, linkId, 1, 0, 3);
        var json = await author.GetStringAsync("/api/v1/export?format=json");

        Assert.Equal(0, (await RestoreAsync(author, json, dryRun: false)).HighlightsAdded);
        Assert.Single(await ListAsync(author, linkId));
    }

    /// <summary>
    /// A mark with no article to sit in has nowhere to go. It is in the file — it is still
    /// somebody's words about the page — but a restore cannot conjure the article back.
    /// </summary>
    [Fact]
    public async Task A_highlight_on_an_article_no_longer_saved_is_exported_but_not_restored()
    {
        var author = await NewUserAsync();
        var linkId = await ArticleAsync(author);
        await MarkedAsync(author, linkId, 1, 0, 3);

        var playlists = (await author.GetFromJsonAsync<PagedPlaylists>("/api/v1/playlists"))!.Items;
        var playlist = Assert.Single(playlists).Id;
        var item = (await author.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items[0];
        (await author.DeleteAsync($"/api/v1/items/{item.Id}")).EnsureSuccessStatusCode();

        var json = await author.GetStringAsync("/api/v1/export?format=json");
        Assert.Contains("\"highlights\"", json);
        Assert.Contains("\"paragraphIndex\":1", json.Replace(" ", ""));

        var restorer = await NewUserAsync();
        Assert.Equal(0, (await RestoreAsync(restorer, json, dryRun: false)).HighlightsAdded);
    }

    private record PagedPlaylists(List<PlaylistDto> Items);

    private record SearchHitDto(Guid ItemId, LinkRef Link);

    private record SearchPage(List<SearchHitDto> Items);

    [Fact]
    public async Task Is_highlighted_finds_only_what_was_marked()
    {
        var client = await NewUserAsync();
        var marked = await ArticleAsync(client);
        var unmarked = await ArticleAsync(client);
        await MarkedAsync(client, marked, 1, 0, 3);

        var page = await client.GetFromJsonAsync<SearchPage>(
            $"/api/v1/search?q={Uri.EscapeDataString("is:highlighted")}");

        var hit = Assert.Single(page!.Items);
        Assert.Equal(marked, hit.Link.Id);
        Assert.DoesNotContain(page.Items, h => h.Link.Id == unmarked);
    }

    /// <summary>
    /// A note beside a passage is the same kind of thing as the note on an item, and it was the
    /// one piece of somebody's own writing that search could not find.
    /// </summary>
    [Fact]
    public async Task What_you_wrote_beside_a_passage_is_searchable()
    {
        var client = await NewUserAsync();
        var linkId = await ArticleAsync(client);
        var word = "zq" + Guid.NewGuid().ToString("N")[..10];
        await MarkedAsync(client, linkId, 1, 0, 3, note: $"Remember {word} for the talk");

        var page = await client.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={word}");

        Assert.Equal(linkId, Assert.Single(page!.Items).Link.Id);
    }

    /// <summary>Somebody else's notes on the same article are theirs, and so is finding it by them.</summary>
    [Fact]
    public async Task Another_persons_note_does_not_find_your_copy()
    {
        var author = await NewUserAsync();
        var linkId = await ArticleAsync(author);
        var word = "zq" + Guid.NewGuid().ToString("N")[..10];
        await MarkedAsync(author, linkId, 1, 0, 3, note: word);

        var other = await NewUserAsync();
        var res = await other.PostAsJsonAsync("/api/v1/playlists", new { name = $"Mine {Guid.NewGuid():N}" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
        (await other.PostAsJsonAsync($"/api/v1/playlists/{playlist}/items", new { url = await UrlOfAsync(linkId) }))
            .EnsureSuccessStatusCode();

        var page = await other.GetFromJsonAsync<SearchPage>($"/api/v1/search?q={word}");

        Assert.Empty(page!.Items);
    }

    private async Task<string> UrlOfAsync(Guid linkId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Links.Where(l => l.Id == linkId).Select(l => l.CanonicalUrl).FirstAsync();
    }
}
