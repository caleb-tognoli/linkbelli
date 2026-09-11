using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Saving a public playlist to a folder files a copy of it; nothing ever said that something new
/// had turned up in it. These cover following, the feed it exists for, and what "new" is measured
/// against.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class FollowTests(PostgresApiFactory factory)
{
    private record FollowStateDto(bool Following, int FollowerCount);

    private record FeedItemDto(Guid ItemId, Guid PlaylistId, string PlaylistName, string OwnerUsername, string Url, string? Title);

    private record FeedDto(List<FeedItemDto> Items, string? NextCursor, int NewCount, DateTimeOffset? LastSeenAt);

    private record FollowedPlaylistDto(Guid PlaylistId, string Name, string OwnerUsername, int ItemCount);

    private record FollowedUserDto(string Username, int PublicPlaylistCount);

    private record FollowingDto(List<FollowedPlaylistDto> Playlists, List<FollowedUserDto> Users);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name, string visibility = "Public")
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<FollowStateDto> FollowPlaylistAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.PostAsync($"/api/v1/playlists/{playlistId}/follow", null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FollowStateDto>())!;
    }

    private static async Task<FollowStateDto> FollowUserAsync(HttpClient client, string username)
    {
        var res = await client.PostAsync($"/api/v1/users/{username}/follow", null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FollowStateDto>())!;
    }

    private static async Task<FeedDto> FeedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<FeedDto>("/api/v1/feed"))!;

    [Fact]
    public async Task Following_a_playlist_puts_its_links_in_your_feed()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Good reading");

        var (follower, _) = await NewUserAsync();
        var state = await FollowPlaylistAsync(follower, playlist);
        Assert.True(state.Following);
        Assert.Equal(1, state.FollowerCount);

        await factory.SeedEnrichedItemsAsync(playlist, 2, _ => "Something new");

        var feed = await FeedAsync(follower);
        Assert.Equal(2, feed.Items.Count);
        Assert.All(feed.Items, item => Assert.Equal("Good reading", item.PlaylistName));
    }

    [Fact]
    public async Task Following_a_person_covers_playlists_they_have_not_made_yet()
    {
        var (owner, ownerName) = await NewUserAsync();

        var (follower, _) = await NewUserAsync();
        await FollowUserAsync(follower, ownerName);

        // Made after the follow — the whole difference between following a person and following
        // each of their lists by hand.
        var later = await NewPlaylistAsync(owner, "Started later");
        await factory.SeedEnrichedItemsAsync(later, 1);

        var feed = await FeedAsync(follower);
        Assert.Single(feed.Items);
        Assert.Equal("Started later", feed.Items[0].PlaylistName);
    }

    [Fact]
    public async Task A_private_playlist_never_reaches_a_feed()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (follower, _) = await NewUserAsync();
        await FollowUserAsync(follower, ownerName);

        var secret = await NewPlaylistAsync(owner, "Private", visibility: "Private");
        await factory.SeedEnrichedItemsAsync(secret, 3);

        // Following someone is not a way into what they didn't publish.
        Assert.Empty((await FeedAsync(follower)).Items);
    }

    [Fact]
    public async Task You_cannot_follow_a_private_playlist()
    {
        var (owner, _) = await NewUserAsync();
        var secret = await NewPlaylistAsync(owner, "Private", visibility: "Private");

        var (stranger, _) = await NewUserAsync();
        var res = await stranger.PostAsync($"/api/v1/playlists/{secret}/follow", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Following_your_own_work_is_refused_rather_than_quietly_allowed()
    {
        var (owner, ownerName) = await NewUserAsync();
        var mine = await NewPlaylistAsync(owner, "Mine");

        // A feed of your own links is not a feed, and letting it happen makes the count wrong.
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsync($"/api/v1/playlists/{mine}/follow", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await owner.PostAsync($"/api/v1/users/{ownerName}/follow", null)).StatusCode);
    }

    [Fact]
    public async Task Following_twice_is_one_follow()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Popular");

        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);
        var again = await FollowPlaylistAsync(follower, playlist);

        Assert.Equal(1, again.FollowerCount);
    }

    [Fact]
    public async Task Unfollowing_empties_the_feed_again()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Briefly followed");
        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        Assert.Single((await FeedAsync(follower)).Items);

        var res = await follower.DeleteAsync($"/api/v1/playlists/{playlist}/follow");
        res.EnsureSuccessStatusCode();
        var state = (await res.Content.ReadFromJsonAsync<FollowStateDto>())!;

        Assert.False(state.Following);
        Assert.Equal(0, state.FollowerCount);
        Assert.Empty((await FeedAsync(follower)).Items);
    }

    [Fact]
    public async Task New_is_measured_against_your_own_last_look()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Ticking over");
        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);

        await factory.SeedEnrichedItemsAsync(playlist, 2);
        Assert.Equal(2, (await FeedAsync(follower)).NewCount);

        (await follower.PostAsync("/api/v1/feed/seen", null)).EnsureSuccessStatusCode();

        var afterLooking = await FeedAsync(follower);
        Assert.Equal(0, afterLooking.NewCount);
        // Still there to read — marking it seen is not marking it gone.
        Assert.Equal(2, afterLooking.Items.Count);
        Assert.NotNull(afterLooking.LastSeenAt);

        await factory.SeedEnrichedItemsAsync(playlist, 1);
        Assert.Equal(1, (await FeedAsync(follower)).NewCount);
    }

    [Fact]
    public async Task Everything_is_new_to_someone_who_has_never_looked()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "First time");
        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        var feed = await FeedAsync(follower);

        Assert.Null(feed.LastSeenAt);
        Assert.Equal(3, feed.NewCount);
    }

    [Fact]
    public async Task The_feed_pages()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Busy");
        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);
        await factory.SeedEnrichedItemsAsync(playlist, 5);

        var first = await follower.GetFromJsonAsync<FeedDto>("/api/v1/feed?limit=2");
        Assert.Equal(2, first!.Items.Count);
        Assert.NotNull(first.NextCursor);

        var second = await follower.GetFromJsonAsync<FeedDto>(
            $"/api/v1/feed?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");

        Assert.Equal(2, second!.Items.Count);
        Assert.Empty(second.Items.Select(i => i.ItemId).Intersect(first.Items.Select(i => i.ItemId)));
    }

    [Fact]
    public async Task What_you_follow_is_listed_back_to_you()
    {
        var (owner, ownerName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Followed list");

        var (follower, _) = await NewUserAsync();
        await FollowPlaylistAsync(follower, playlist);
        await FollowUserAsync(follower, ownerName);

        var following = await follower.GetFromJsonAsync<FollowingDto>("/api/v1/me/following");

        Assert.Equal("Followed list", following!.Playlists.Single().Name);
        Assert.Equal(ownerName, following.Users.Single().Username, ignoreCase: true);
    }

    [Fact]
    public async Task Following_somebody_who_does_not_exist_says_so()
    {
        var (client, _) = await NewUserAsync();

        var res = await client.PostAsync("/api/v1/users/nobody-by-that-name/follow", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
