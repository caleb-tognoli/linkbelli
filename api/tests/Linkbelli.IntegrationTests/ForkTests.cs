using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Taking a copy of somebody's public playlist.
/// </summary>
/// <remarks>
/// Discovery ranks public playlists four ways, recommends similar ones, gives each a profile page
/// and three syndicated feeds — and the only things you could then do with a list you liked were
/// follow it, which is a stream of what it gains <em>next</em> rather than the thing you just
/// found, or copy the links one at a time.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ForkTests(PostgresApiFactory factory)
{
    private record PlaylistDetail(
        Guid Id, string Name, string Slug, string Visibility, int ItemCount, string[] Tags,
        int ForkCount, Guid? ForkedFromPlaylistId);

    private record ItemDto(Guid Id, string? Note, int? Score, string Status, LinkRef Link, string? AddedBy);

    private record LinkRef(Guid Id, string Url);

    private record PagedItems(List<ItemDto> Items);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));
        return (client, username);
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(
        HttpClient client, string name, string visibility, params string[] tags)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility, tags });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private static Task<HttpResponseMessage> ForkAsync(HttpClient client, string username, string slug) =>
        client.PostAsync($"/api/v1/public/playlists/{username}/{slug}/fork", null);

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    [Fact]
    public async Task A_fork_arrives_with_the_same_links_in_the_same_order()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Good stuff {Guid.NewGuid():N}", "Public");
        var seeded = await factory.SeedEnrichedItemsAsync(original.Id, 3);
        var theirs = await ItemsAsync(author, original.Id);

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();
        var fork = (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!;

        Assert.Equal(original.Name, fork.Name);
        Assert.Equal(3, fork.ItemCount);
        Assert.Equal(seeded.Count, theirs.Count);

        var mine = await ItemsAsync(reader, fork.Id);
        Assert.Equal(
            theirs.Select(i => i.Link.Url).ToArray(),
            mine.Select(i => i.Link.Url).ToArray());
    }

    /// <summary>
    /// Republishing somebody's list under your own name is a decision for the person who took
    /// the copy, not a side effect of taking it.
    /// </summary>
    [Fact]
    public async Task A_fork_is_private_until_its_new_owner_says_otherwise()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Published {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();

        Assert.Equal("Private", (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!.Visibility);
    }

    /// <summary>
    /// The note and the score are the original owner's opinions, and the status is their reading
    /// history. Inheriting them would put words in the new owner's mouth and mark things read
    /// that they have never opened.
    /// </summary>
    [Fact]
    public async Task Notes_scores_and_reading_history_do_not_come_across()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, readerName) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Annotated {Guid.NewGuid():N}", "Public");
        var seeded = await factory.SeedEnrichedItemsAsync(original.Id, 1);

        (await author.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = "I loved this", status = "Watched" }))
            .EnsureSuccessStatusCode();
        (await author.PutAsJsonAsync($"/api/v1/items/{seeded[0]}/score", new { score = 95 }))
            .EnsureSuccessStatusCode();

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();
        var fork = (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!;

        var copied = Assert.Single(await ItemsAsync(reader, fork.Id));
        Assert.Null(copied.Note);
        Assert.Null(copied.Score);
        Assert.Equal("Added", copied.Status);

        // And it is the forker's own row: they put it there.
        Assert.Equal(readerName, copied.AddedBy, ignoreCase: true);
    }

    [Fact]
    public async Task The_tags_come_across()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();
        var tag = "topic" + Guid.NewGuid().ToString("N")[..8];

        var original = await NewPlaylistAsync(author, $"Tagged {Guid.NewGuid():N}", "Public", tag);
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();

        Assert.Contains(tag, (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!.Tags);
    }

    /// <summary>
    /// Unlisted is share-by-link. Somebody holding the link may read it; taking a permanent copy
    /// is a different thing from being shown it once.
    /// </summary>
    [Theory]
    [InlineData("Unlisted")]
    [InlineData("Private")]
    public async Task Only_a_public_playlist_can_be_forked(string visibility)
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Not public {Guid.NewGuid():N}", visibility);
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        Assert.Equal(HttpStatusCode.NotFound, (await ForkAsync(reader, authorName, original.Slug)).StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_asked_to_sign_in()
    {
        var (author, authorName) = await NewUserAsync();
        var original = await NewPlaylistAsync(author, $"Public {Guid.NewGuid():N}", "Public");

        var res = await ForkAsync(factory.CreateClient(), authorName, original.Slug);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    /// <summary>
    /// The number the original's owner gets to see — worth more than the like count, because a
    /// like is a moment's approval and a fork is somebody deciding to keep it.
    /// </summary>
    [Fact]
    public async Task The_original_learns_how_many_people_kept_a_copy()
    {
        var (author, authorName) = await NewUserAsync();
        var (first, _) = await NewUserAsync();
        var (second, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Popular {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        (await ForkAsync(first, authorName, original.Slug)).EnsureSuccessStatusCode();
        (await ForkAsync(second, authorName, original.Slug)).EnsureSuccessStatusCode();

        var mine = await author.GetFromJsonAsync<PlaylistDetail>($"/api/v1/playlists/{original.Id}");

        Assert.Equal(2, mine!.ForkCount);
    }

    [Fact]
    public async Task A_fork_remembers_where_it_came_from()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Source {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();
        var fork = (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!;

        var reread = await reader.GetFromJsonAsync<PlaylistDetail>($"/api/v1/playlists/{fork.Id}");
        Assert.Equal(original.Id, reread!.ForkedFromPlaylistId);
    }

    /// <summary>
    /// Forking twice is not an error — people do it — and the second copy must not collide with
    /// the first on the slug.
    /// </summary>
    [Fact]
    public async Task Forking_the_same_list_twice_gives_two_playlists()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Twice {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(original.Id, 1);

        var one = await ForkAsync(reader, authorName, original.Slug);
        var two = await ForkAsync(reader, authorName, original.Slug);
        one.EnsureSuccessStatusCode();
        two.EnsureSuccessStatusCode();

        var a = (await one.Content.ReadFromJsonAsync<PlaylistDetail>())!;
        var b = (await two.Content.ReadFromJsonAsync<PlaylistDetail>())!;

        Assert.NotEqual(a.Id, b.Id);
        Assert.NotEqual(a.Slug, b.Slug);
    }

    /// <summary>
    /// Links are globally deduplicated, so forking writes item rows and no link rows. Which is
    /// what makes copying a five-hundred-item list cheap enough to offer as a button.
    /// </summary>
    [Fact]
    public async Task A_fork_points_at_the_same_links_rather_than_copies_of_them()
    {
        var (author, authorName) = await NewUserAsync();
        var (reader, _) = await NewUserAsync();

        var original = await NewPlaylistAsync(author, $"Shared links {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(original.Id, 2);

        var res = await ForkAsync(reader, authorName, original.Slug);
        res.EnsureSuccessStatusCode();
        var fork = (await res.Content.ReadFromJsonAsync<PlaylistDetail>())!;

        var theirs = (await ItemsAsync(author, original.Id)).Select(i => i.Link.Id).Order();
        var mine = (await ItemsAsync(reader, fork.Id)).Select(i => i.Link.Id).Order();

        Assert.Equal(theirs, mine);
    }
}
