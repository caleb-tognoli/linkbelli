using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Nothing anyone did on a public playlist was visible to its owner, or to anyone else browsing.
/// These cover the lightest signal there is, and the two things that keep the number meaning
/// something: one per person, and only on lists you were actually shown.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistLikeTests(PostgresApiFactory factory)
{
    private record LikeDto(Guid PlaylistId, int LikeCount, bool LikedByMe);

    private record PublicPlaylistDto(Guid Id, string Name, int LikeCount, bool LikedByMe);

    private record DiscoverRow(string OwnerUsername, string Slug, string Name, int LikeCount);

    private record DiscoverPage(List<DiscoverRow> Items);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<(Guid Id, string Slug)> NewPublicPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Public" });
        res.EnsureSuccessStatusCode();
        var created = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        return (created.Id, created.Slug);
    }

    private static async Task<LikeDto> LikeAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.PostAsync($"/api/v1/playlists/{playlistId}/like", null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LikeDto>())!;
    }

    private static async Task<LikeDto> UnlikeAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.DeleteAsync($"/api/v1/playlists/{playlistId}/like");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LikeDto>())!;
    }

    [Fact]
    public async Task Someone_else_can_like_a_public_playlist()
    {
        var (owner, username) = await NewUserAsync();
        var (id, slug) = await NewPublicPlaylistAsync(owner, "Worth reading");

        var (visitor, _) = await NewUserAsync();
        var liked = await LikeAsync(visitor, id);

        Assert.Equal(1, liked.LikeCount);
        Assert.True(liked.LikedByMe);

        var seen = await visitor.GetFromJsonAsync<PublicPlaylistDto>(
            $"/api/v1/public/playlists/{username}/{slug}");
        Assert.Equal(1, seen!.LikeCount);
        Assert.True(seen.LikedByMe);
    }

    [Fact]
    public async Task Liking_twice_is_one_like()
    {
        var (owner, _) = await NewUserAsync();
        var (id, _) = await NewPublicPlaylistAsync(owner, "Double tap");

        var (visitor, _) = await NewUserAsync();
        await LikeAsync(visitor, id);
        var again = await LikeAsync(visitor, id);

        // A double tap should not be a way to inflate a number.
        Assert.Equal(1, again.LikeCount);
    }

    [Fact]
    public async Task A_like_can_be_taken_back()
    {
        var (owner, _) = await NewUserAsync();
        var (id, _) = await NewPublicPlaylistAsync(owner, "Changed my mind");

        var (visitor, _) = await NewUserAsync();
        await LikeAsync(visitor, id);
        var unliked = await UnlikeAsync(visitor, id);

        Assert.Equal(0, unliked.LikeCount);
        Assert.False(unliked.LikedByMe);

        // Idempotent both ways: unliking what you never liked is not an error.
        Assert.Equal(0, (await UnlikeAsync(visitor, id)).LikeCount);
    }

    [Fact]
    public async Task Likes_from_different_people_add_up()
    {
        var (owner, username) = await NewUserAsync();
        var (id, slug) = await NewPublicPlaylistAsync(owner, "Popular");

        foreach (var _ in Enumerable.Range(0, 3))
        {
            var (visitor, _) = await NewUserAsync();
            await LikeAsync(visitor, id);
        }

        var seen = await owner.GetFromJsonAsync<PublicPlaylistDto>(
            $"/api/v1/public/playlists/{username}/{slug}");

        Assert.Equal(3, seen!.LikeCount);
        // The owner hasn't liked their own list, and the flag is about the caller.
        Assert.False(seen.LikedByMe);
    }

    [Fact]
    public async Task You_cannot_like_a_playlist_you_were_never_shown()
    {
        var (owner, _) = await NewUserAsync();
        var res = await owner.PostAsJsonAsync("/api/v1/playlists", new { name = "Private", visibility = "Private" });
        res.EnsureSuccessStatusCode();
        var privatePlaylist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;

        var (stranger, _) = await NewUserAsync();
        var attempt = await stranger.PostAsync($"/api/v1/playlists/{privatePlaylist.Id}/like", null);

        // You cannot vote on something you were never shown — and a 404 doesn't confirm it exists.
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_visitor_sees_the_count_but_cannot_add_to_it()
    {
        var (owner, username) = await NewUserAsync();
        var (id, slug) = await NewPublicPlaylistAsync(owner, "Read only");
        var (visitor, _) = await NewUserAsync();
        await LikeAsync(visitor, id);

        var anonymous = factory.CreateClient();

        var seen = await anonymous.GetFromJsonAsync<PublicPlaylistDto>(
            $"/api/v1/public/playlists/{username}/{slug}");
        Assert.Equal(1, seen!.LikeCount);
        Assert.False(seen.LikedByMe);

        var attempt = await anonymous.PostAsync($"/api/v1/playlists/{id}/like", null);
        Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
    }

    [Fact]
    public async Task Discovery_reports_the_count_too()
    {
        var (owner, _) = await NewUserAsync();
        var name = $"Findable {Guid.NewGuid():N}";
        var (id, _) = await NewPublicPlaylistAsync(owner, name);

        var (visitor, _) = await NewUserAsync();
        await LikeAsync(visitor, id);

        var page = await visitor.GetFromJsonAsync<DiscoverPage>(
            $"/api/v1/public/playlists?q={Uri.EscapeDataString(name)}");

        Assert.Equal(1, page!.Items.Single().LikeCount);
    }
}
