using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Sending one saved link used to mean making a whole playlist public, or pasting a bare address
/// and losing the note that was the reason for sending it. These cover the link a share produces,
/// what it discloses, and what revoking it does.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemShareTests(PostgresApiFactory factory)
{
    private record ShareDto(Guid ItemId, string Token, DateTimeOffset? SharedAt);

    private record SharedDto(
        string Url, string Host, string? Title, string? Description, Guid? ThumbnailLinkId,
        string? SiteName, string? Note, string SharedBy, DateTimeOffset SharedAt, bool Nsfw,
        string Kind, int? WordCount);

    private record ItemDto(Guid Id, string? Note, string? ShareToken);

    private record PagedItems(List<ItemDto> Items);

    private async Task<HttpClient> NewUserAsync(string? username = null)
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(username ?? NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    private static async Task<ShareDto> ShareAsync(HttpClient client, Guid itemId)
    {
        var res = await client.PostAsync($"/api/v1/items/{itemId}/share", null);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ShareDto>())!;
    }

    /// <summary>An anonymous visitor: a share link is opened by people with no account here.</summary>
    private HttpClient Visitor() => factory.CreateClient();

    [Fact]
    public async Task A_shared_link_opens_for_someone_with_no_account()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1, _ => "Worth your time");
        var item = (await ItemsAsync(client, playlist))[0];

        var share = await ShareAsync(client, item.Id);
        var shared = await Visitor().GetFromJsonAsync<SharedDto>($"/api/v1/public/items/{share.Token}");

        Assert.Equal("Worth your time", shared!.Title);
        Assert.NotEmpty(shared.SharedBy);
    }

    [Fact]
    public async Task The_note_travels_with_it()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(client, playlist))[0];

        (await client.PatchAsJsonAsync($"/api/v1/items/{item.Id}", new { note = "Read the third section." }))
            .EnsureSuccessStatusCode();

        var share = await ShareAsync(client, item.Id);
        var shared = await Visitor().GetFromJsonAsync<SharedDto>($"/api/v1/public/items/{share.Token}");

        // The note is usually the reason for sending it; a bare address throws that away.
        Assert.Equal("Read the third section.", shared!.Note);
    }

    [Fact]
    public async Task Sharing_twice_gives_back_the_same_link()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(client, playlist))[0];

        var first = await ShareAsync(client, item.Id);
        var second = await ShareAsync(client, item.Id);

        // A second share that rotated the token would break a link already sent to someone.
        Assert.Equal(first.Token, second.Token);
    }

    [Fact]
    public async Task Revoking_stops_the_link_working()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(client, playlist))[0];
        var share = await ShareAsync(client, item.Id);

        (await client.DeleteAsync($"/api/v1/items/{item.Id}/share")).EnsureSuccessStatusCode();

        var res = await Visitor().GetAsync($"/api/v1/public/items/{share.Token}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_revoked_link_is_indistinguishable_from_one_that_never_existed()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(client, playlist))[0];
        var share = await ShareAsync(client, item.Id);
        (await client.DeleteAsync($"/api/v1/items/{item.Id}/share")).EnsureSuccessStatusCode();

        var visitor = Visitor();
        var revoked = await visitor.GetAsync($"/api/v1/public/items/{share.Token}");
        var neverExisted = await visitor.GetAsync("/api/v1/public/items/PGqLo3zNRk6cEweJ0yZL1A");

        // A revoked share should not confirm that it once pointed at something. Compared on the
        // message rather than the whole body, which carries a per-request trace id.
        Assert.Equal(neverExisted.StatusCode, revoked.StatusCode);
        Assert.Equal(await DetailAsync(neverExisted), await DetailAsync(revoked));
    }

    /// <summary>The human-readable message out of a problem response.</summary>
    private static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDto>())?.Detail;

    private record ProblemDto(string? Detail);

    [Fact]
    public async Task A_private_playlist_stays_private_when_one_of_its_links_is_shared()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Private reading");
        await factory.SeedEnrichedItemsAsync(playlist, 3);
        var items = await ItemsAsync(client, playlist);

        var share = await ShareAsync(client, items[0].Id);
        var shared = await Visitor().GetFromJsonAsync<SharedDto>($"/api/v1/public/items/{share.Token}");

        // One link, not a way into the list it came from.
        Assert.NotNull(shared);
        var serialized = System.Text.Json.JsonSerializer.Serialize(shared);
        Assert.DoesNotContain(playlist.ToString(), serialized);
        Assert.DoesNotContain("Private reading", serialized);
    }

    [Fact]
    public async Task Only_the_owner_can_share_their_item()
    {
        var owner = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Mine");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(owner, playlist))[0];

        var stranger = await NewUserAsync();
        var res = await stranger.PostAsync($"/api/v1/items/{item.Id}/share", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task The_item_says_whether_it_is_shared()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var item = (await ItemsAsync(client, playlist))[0];

        Assert.Null(item.ShareToken);

        await ShareAsync(client, item.Id);

        // Otherwise the owner has no way to tell what they have already put out there.
        Assert.NotNull((await ItemsAsync(client, playlist))[0].ShareToken);
    }

    [Fact]
    public async Task Tokens_are_not_guessable_from_one_another()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        await factory.SeedEnrichedItemsAsync(playlist, 3);
        var items = await ItemsAsync(client, playlist);

        var tokens = new List<string>();
        foreach (var item in items)
        {
            tokens.Add((await ShareAsync(client, item.Id)).Token);
        }

        Assert.Equal(3, tokens.Distinct().Count());
        Assert.All(tokens, token => Assert.True(token.Length >= 20, $"Token '{token}' is too short."));
    }
}
