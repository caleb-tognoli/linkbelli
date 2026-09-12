using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A playlist looked like every other playlist: a name and a count. These cover choosing one of
/// its own links to stand for it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistCoverTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name, string Slug, Guid? CoverLinkId);

    private record ItemDto(Guid Id, LinkDto Link);

    private record LinkDto(Guid Id, string Url);

    private record PagedItems(List<ItemDto> Items);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(
        HttpClient client, string name, string visibility = "Private")
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    private static async Task<HttpResponseMessage> SetCoverAsync(
        HttpClient client, Guid playlistId, Guid? linkId) =>
        await client.PatchAsJsonAsync($"/api/v1/playlists/{playlistId}", new { coverLinkId = linkId });

    [Fact]
    public async Task A_playlist_starts_with_no_cover_of_its_own()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Uncovered");

        // Null means "whatever the first item with an image happens to be" — a guess, which the
        // owner can replace with a decision.
        Assert.Null(playlist.CoverLinkId);
    }

    [Fact]
    public async Task One_of_its_own_links_can_be_chosen()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Covered");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);
        var items = await ItemsAsync(client, playlist.Id);

        (await SetCoverAsync(client, playlist.Id, items[1].Link.Id)).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<PlaylistViewDto>($"/api/v1/playlists/{playlist.Id}");
        Assert.Equal(items[1].Link.Id, after!.CoverLinkId);
    }

    [Fact]
    public async Task A_link_from_somewhere_else_is_refused()
    {
        var (client, _) = await NewUserAsync();
        var subject = await NewPlaylistAsync(client, "Subject");
        var other = await NewPlaylistAsync(client, "Other");
        await factory.SeedEnrichedItemsAsync(subject.Id, 1);
        await factory.SeedEnrichedItemsAsync(other.Id, 1);
        var elsewhere = (await ItemsAsync(client, other.Id))[0].Link.Id;

        var res = await SetCoverAsync(client, subject.Id, elsewhere);

        // A cover is chosen from what is in the playlist — which is also what stops it pointing
        // at somebody else's picture.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task It_can_be_cleared_again()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Cleared");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);
        var link = (await ItemsAsync(client, playlist.Id))[0].Link.Id;

        (await SetCoverAsync(client, playlist.Id, link)).EnsureSuccessStatusCode();
        (await SetCoverAsync(client, playlist.Id, Guid.Empty)).EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<PlaylistViewDto>($"/api/v1/playlists/{playlist.Id}");
        Assert.Null(after!.CoverLinkId);
    }

    [Fact]
    public async Task Leaving_it_out_of_an_edit_does_not_clear_it()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Kept");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);
        var link = (await ItemsAsync(client, playlist.Id))[0].Link.Id;
        (await SetCoverAsync(client, playlist.Id, link)).EnsureSuccessStatusCode();

        // Renaming is not a statement about the cover.
        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist.Id}", new { name = "Renamed" }))
            .EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<PlaylistViewDto>($"/api/v1/playlists/{playlist.Id}");
        Assert.Equal(link, after!.CoverLinkId);
    }

    [Fact]
    public async Task A_visitor_sees_the_cover_the_owner_chose()
    {
        var (owner, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Public cover {Guid.NewGuid():N}", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);
        var link = (await ItemsAsync(owner, playlist.Id))[1].Link.Id;
        (await SetCoverAsync(owner, playlist.Id, link)).EnsureSuccessStatusCode();

        var seen = await factory.CreateClient()
            .GetFromJsonAsync<PlaylistViewDto>($"/api/v1/public/playlists/{username}/{playlist.Slug}");

        Assert.Equal(link, seen!.CoverLinkId);
    }

    [Fact]
    public async Task Somebody_elses_playlist_cannot_be_given_a_cover()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Theirs");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);
        var link = (await ItemsAsync(owner, playlist.Id))[0].Link.Id;

        var (stranger, _) = await NewUserAsync();
        var res = await SetCoverAsync(stranger, playlist.Id, link);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
