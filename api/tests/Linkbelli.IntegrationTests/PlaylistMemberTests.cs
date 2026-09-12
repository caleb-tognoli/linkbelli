using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Sharing was all-or-nothing public: showing one list to one person meant publishing it to
/// everyone, and collaborating on one meant handing over an account. These cover what each role
/// can actually do, and the lines between them.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistMemberTests(PostgresApiFactory factory)
{
    private record MemberDto(string Username, string Role, DateTimeOffset AddedAt);

    private record SharedDto(Guid PlaylistId, string Name, string OwnerUsername, string Role, int ItemCount);

    private record ItemDto(Guid Id, string? Note);

    private record PagedItems(List<ItemDto> Items);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<HttpResponseMessage> ShareAsync(
        HttpClient owner, Guid playlistId, string username, string role) =>
        await owner.PutAsJsonAsync($"/api/v1/playlists/{playlistId}/members/{username}", new { role });

    private static async Task AddLinkAsync(HttpClient client, Guid playlistId, string url)
    {
        var res = await client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/items", new { url });
        res.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> TryAddLinkAsync(
        HttpClient client, Guid playlistId, string url) =>
        await client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/items", new { url });

    private static string NewUrl() => $"https://members.example/{Guid.NewGuid():N}";

    [Fact]
    public async Task A_viewer_can_read_it_and_nothing_else()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Shared reading");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var (viewer, viewerName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, viewerName, "Viewer")).EnsureSuccessStatusCode();

        var items = await viewer.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        Assert.Equal(2, items!.Items.Count);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await TryAddLinkAsync(viewer, playlist, NewUrl())).StatusCode);
    }

    [Fact]
    public async Task A_contributor_can_add_but_not_remove()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Collecting together");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var existing = (await owner.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items[0];

        var (helper, helperName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, helperName, "Contributor")).EnsureSuccessStatusCode();

        await AddLinkAsync(helper, playlist, NewUrl());

        // "Help me collect things" should not also mean "delete things".
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await helper.DeleteAsync($"/api/v1/items/{existing.Id}")).StatusCode);
    }

    [Fact]
    public async Task An_editor_can_change_and_remove_items()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Edited together");
        await factory.SeedEnrichedItemsAsync(playlist, 2);
        var items = (await owner.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items;

        var (editor, editorName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, editorName, "Editor")).EnsureSuccessStatusCode();

        (await editor.PatchAsJsonAsync($"/api/v1/items/{items[0].Id}", new { note = "Edited" }))
            .EnsureSuccessStatusCode();
        (await editor.DeleteAsync($"/api/v1/items/{items[1].Id}")).EnsureSuccessStatusCode();

        var left = await owner.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        Assert.Single(left!.Items);
        Assert.Equal("Edited", left.Items[0].Note);
    }

    [Fact]
    public async Task An_editor_can_rename_it_but_cannot_publish_it()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Private working list");

        var (editor, editorName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, editorName, "Editor")).EnsureSuccessStatusCode();

        (await editor.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { name = "Renamed" }))
            .EnsureSuccessStatusCode();

        // Who a playlist is shared with is the owner's decision, not an editing action.
        var published = await editor.PatchAsJsonAsync(
            $"/api/v1/playlists/{playlist}", new { visibility = "Public" });

        Assert.Equal(HttpStatusCode.BadRequest, published.StatusCode);
    }

    [Fact]
    public async Task A_stranger_sees_nothing()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Not shared with you");

        var (stranger, _) = await NewUserAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/playlists/{playlist}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/playlists/{playlist}/items")).StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_share_it()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Mine to share");

        var (editor, editorName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, editorName, "Editor")).EnsureSuccessStatusCode();

        var (outsider, outsiderName) = await NewUserAsync();

        // An editor edits the playlist; they do not get to hand out keys to it.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await ShareAsync(editor, playlist, outsiderName, "Viewer")).StatusCode);
    }

    [Fact]
    public async Task A_role_can_be_changed_without_removing_the_person()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Promoted");

        var (helper, helperName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, helperName, "Viewer")).EnsureSuccessStatusCode();
        (await ShareAsync(owner, playlist, helperName, "Editor")).EnsureSuccessStatusCode();

        var members = await owner.GetFromJsonAsync<List<MemberDto>>($"/api/v1/playlists/{playlist}/members");
        Assert.Equal("Editor", members!.Single().Role);
    }

    [Fact]
    public async Task Everyone_in_a_playlist_can_see_who_else_is()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Who else");
        var (viewer, viewerName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, viewerName, "Viewer")).EnsureSuccessStatusCode();

        // Being in a shared list without knowing who else can read it is worse than not sharing.
        var members = await viewer.GetFromJsonAsync<List<MemberDto>>($"/api/v1/playlists/{playlist}/members");

        Assert.Equal(viewerName, members!.Single().Username, ignoreCase: true);
    }

    [Fact]
    public async Task Someone_can_let_themselves_out()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Leaving");
        var (member, memberName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, memberName, "Editor")).EnsureSuccessStatusCode();

        // Leaving should never require asking the person you are leaving.
        (await member.DeleteAsync($"/api/v1/playlists/{playlist}/members/{memberName}"))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await member.GetAsync($"/api/v1/playlists/{playlist}")).StatusCode);
    }

    [Fact]
    public async Task Removing_someone_takes_their_access_with_it()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Revoked");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var (member, memberName) = await NewUserAsync();
        (await ShareAsync(owner, playlist, memberName, "Editor")).EnsureSuccessStatusCode();

        (await member.GetAsync($"/api/v1/playlists/{playlist}/items")).EnsureSuccessStatusCode();

        (await owner.DeleteAsync($"/api/v1/playlists/{playlist}/members/{memberName}"))
            .EnsureSuccessStatusCode();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await member.GetAsync($"/api/v1/playlists/{playlist}/items")).StatusCode);
    }

    [Fact]
    public async Task Shared_playlists_are_findable_without_being_mixed_into_your_own()
    {
        var (owner, ownerName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Theirs");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var (member, memberName) = await NewUserAsync();
        var mine = await NewPlaylistAsync(member, "Mine");
        (await ShareAsync(owner, playlist, memberName, "Viewer")).EnsureSuccessStatusCode();

        var shared = await member.GetFromJsonAsync<List<SharedDto>>("/api/v1/me/shared");
        Assert.NotNull(shared);
        var only = Assert.Single(shared);
        Assert.Equal("Theirs", only.Name);
        Assert.Equal(ownerName, only.OwnerUsername, ignoreCase: true);
        Assert.Equal(2, only.ItemCount);

        // Their own list is still their own: someone else's playlist does not appear in it.
        var own = await member.GetFromJsonAsync<PagedPlaylistsDto>("/api/v1/playlists");
        Assert.Equal([mine], own!.Items.Select(p => p.Id).ToArray());
    }

    private record PagedPlaylistsDto(List<PlaylistDto> Items);

    [Fact]
    public async Task Sharing_with_yourself_is_refused()
    {
        var (owner, ownerName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Already mine");

        var res = await ShareAsync(owner, playlist, ownerName, "Editor");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Sharing_with_somebody_who_does_not_exist_says_so()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Typo");

        var res = await ShareAsync(owner, playlist, "nobody-by-that-name", "Viewer");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
