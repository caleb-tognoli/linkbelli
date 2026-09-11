using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// View preferences lived in a 50-entry browser cookie: sent on every request, evicting the
/// oldest playlist once full, and never following anyone to another device. These cover the
/// account-scoped replacement.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistViewPreferenceTests(PostgresApiFactory factory)
{
    private record ViewDto(string? Sort, string? Source, string? Status, bool ShowUrls, bool ShowThumbnails);
    private record PlaylistWithViewDto(Guid Id, string Name, ViewDto? View);

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

    private static async Task<PlaylistWithViewDto> ReadAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PlaylistWithViewDto>($"/api/v1/playlists/{playlistId}"))!;

    [Fact]
    public async Task A_playlist_with_no_saved_view_reports_none()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Fresh");

        Assert.Null((await ReadAsync(client, playlist)).View);
    }

    [Fact]
    public async Task A_saved_view_comes_back_on_the_playlist_read()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Remembered");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "score-desc",
            source = "manual",
            status = "Unwatched",
            showUrls = true,
            showThumbnails = false,
        })).EnsureSuccessStatusCode();

        // On the playlist read itself, so opening a playlist doesn't need a second round trip.
        var view = (await ReadAsync(client, playlist)).View;

        Assert.NotNull(view);
        Assert.Equal("score-desc", view!.Sort);
        Assert.Equal("manual", view.Source);
        Assert.Equal("Unwatched", view.Status);
        Assert.True(view.ShowUrls);
        Assert.False(view.ShowThumbnails);
    }

    [Fact]
    public async Task Saving_again_replaces_the_view_rather_than_merging()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Replaced");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "shuffle", source = "manual", status = "Watched", showUrls = true, showThumbnails = false,
        })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "position", source = (string?)null, status = (string?)null, showUrls = false, showThumbnails = true,
        })).EnsureSuccessStatusCode();

        var view = (await ReadAsync(client, playlist)).View!;
        Assert.Equal("position", view.Sort);
        Assert.Null(view.Source);
        Assert.Null(view.Status);
        Assert.False(view.ShowUrls);
        Assert.True(view.ShowThumbnails);
    }

    [Fact]
    public async Task Each_playlist_keeps_its_own_view()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{reading}/view", new
        {
            sort = "date-asc", showUrls = true, showThumbnails = false,
        })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync($"/api/v1/playlists/{watching}/view", new
        {
            sort = "shuffle", showUrls = false, showThumbnails = true,
        })).EnsureSuccessStatusCode();

        Assert.Equal("date-asc", (await ReadAsync(client, reading)).View!.Sort);
        Assert.Equal("shuffle", (await ReadAsync(client, watching)).View!.Sort);
    }

    [Fact]
    public async Task A_view_follows_the_account_not_the_browser()
    {
        var username = NewUsername();

        var laptop = factory.CreateClient();
        var token = await laptop.RegisterAndLoginAsync(username);
        laptop.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var playlist = await NewPlaylistAsync(laptop, "Across devices");
        (await laptop.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "score-desc", showUrls = true, showThumbnails = true,
        })).EnsureSuccessStatusCode();

        // A second sign-in, carrying none of the first one's cookies.
        var phone = factory.CreateClient();
        var phoneToken = await phone.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        phoneToken.EnsureSuccessStatusCode();
        var tokens = (await phoneToken.Content.ReadFromJsonAsync<TokenDto>())!;
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        Assert.Equal("score-desc", (await ReadAsync(phone, playlist)).View!.Sort);
    }

    [Fact]
    public async Task Views_do_not_cross_between_accounts()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var playlist = await NewPlaylistAsync(owner, "Mine");
        (await owner.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "shuffle", showUrls = true, showThumbnails = false,
        })).EnsureSuccessStatusCode();

        var res = await stranger.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "date-asc", showUrls = false, showThumbnails = true,
        });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("shuffle", (await ReadAsync(owner, playlist)).View!.Sort);
    }

    [Fact]
    public async Task An_absurdly_long_value_is_truncated_rather_than_rejected()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Long values");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = new string('x', 500), showUrls = false, showThumbnails = true,
        })).EnsureSuccessStatusCode();

        // A nonsense sort is ignored by the reader anyway; failing the save would be worse than
        // storing something that doesn't fit.
        var view = (await ReadAsync(client, playlist)).View!;
        Assert.True(view.Sort!.Length <= 32);
    }
}
