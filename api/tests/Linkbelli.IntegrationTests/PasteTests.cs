using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The two ways in were one link at a time, and exporting a file to import it. What people
/// actually have is a chat log, a list of tabs or an email. These cover pasting one.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PasteTests(PostgresApiFactory factory)
{
    private record PasteDto(int Found, int Added, int AlreadyThere, List<string> Rejected);

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

    private static async Task<HttpResponseMessage> PasteAsync(HttpClient client, Guid playlistId, string text) =>
        await client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/items/paste", new { text });

    private async Task<int> ItemCountAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlistId);
    }

    [Fact]
    public async Task Every_address_in_the_text_is_added()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Pasted");
        var tag = Guid.NewGuid().ToString("N");

        var res = await PasteAsync(client, playlist, $"""
            Worth a look: https://paste.example/{tag}/one
            and https://paste.example/{tag}/two, plus https://paste.example/{tag}/three.
            """);
        res.EnsureSuccessStatusCode();

        var result = (await res.Content.ReadFromJsonAsync<PasteDto>())!;
        Assert.Equal(3, result.Found);
        Assert.Equal(3, result.Added);
        Assert.Equal(3, await ItemCountAsync(playlist));
    }

    [Fact]
    public async Task Links_already_in_the_playlist_are_counted_not_duplicated()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Repeats");
        var url = $"https://paste.example/{Guid.NewGuid():N}";

        await PasteAsync(client, playlist, url);
        var second = await PasteAsync(client, playlist, $"{url}\nhttps://paste.example/{Guid.NewGuid():N}");
        second.EnsureSuccessStatusCode();

        var result = (await second.Content.ReadFromJsonAsync<PasteDto>())!;
        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.AlreadyThere);
        Assert.Equal(2, await ItemCountAsync(playlist));
    }

    [Fact]
    public async Task What_could_not_be_read_is_named_rather_than_dropped()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mixed");

        // An unparseable authority. (A link-local address is refused later, by the outbound
        // client — it saves fine, and then fails to enrich, exactly like one added by hand.)
        var res = await PasteAsync(client, playlist, $"""
            https://paste.example/{Guid.NewGuid():N}
            https://[not-an-address]/x
            """);
        res.EnsureSuccessStatusCode();

        var result = (await res.Content.ReadFromJsonAsync<PasteDto>())!;

        // A paste of forty links that quietly becomes thirty-nine is worse than one that says
        // which it could not take.
        Assert.Equal(2, result.Found);
        Assert.Equal(1, result.Added);
        Assert.Single(result.Rejected);
    }

    [Fact]
    public async Task Text_with_nothing_in_it_says_so()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Empty");

        var res = await PasteAsync(client, playlist, "just some words, no links");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task The_same_address_twice_in_one_paste_is_added_once()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Doubled");
        var url = $"https://paste.example/{Guid.NewGuid():N}";

        var res = await PasteAsync(client, playlist, $"{url} and again {url}");
        res.EnsureSuccessStatusCode();

        Assert.Equal(1, (await res.Content.ReadFromJsonAsync<PasteDto>())!.Added);
    }

    [Fact]
    public async Task Someone_elses_playlist_is_not_pasteable_into()
    {
        var owner = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Theirs");

        var stranger = await NewUserAsync();
        var res = await PasteAsync(stranger, playlist, "https://paste.example/nope");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_contributor_can_paste_into_a_shared_playlist()
    {
        var owner = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, "Collecting together");

        var helper = await NewUserAsync();
        var username = await UsernameAsync(helper);
        (await owner.PutAsJsonAsync($"/api/v1/playlists/{playlist}/members/{username}", new { role = "Contributor" }))
            .EnsureSuccessStatusCode();

        var res = await PasteAsync(helper, playlist, $"https://paste.example/{Guid.NewGuid():N}");

        res.EnsureSuccessStatusCode();
    }

    private static async Task<string> UsernameAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!.Username;

    private record MeDto(string Username);
}
