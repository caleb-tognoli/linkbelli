using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The chosen layout travels with the other view preferences, so a playlist of videos stays a
/// grid and a reading queue stays a list, on whatever device you open them.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistViewModeTests(PostgresApiFactory factory)
{
    private record ViewDto(string? Sort, string? Source, string? Status, bool ShowUrls, bool ShowThumbnails, string? ViewMode);
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

    [Fact]
    public async Task A_chosen_layout_is_remembered()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Videos");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "date-desc", showUrls = false, showThumbnails = true, viewMode = "grid",
        })).EnsureSuccessStatusCode();

        var read = await client.GetFromJsonAsync<PlaylistWithViewDto>($"/api/v1/playlists/{playlist}");

        Assert.Equal("grid", read!.View!.ViewMode);
    }

    [Fact]
    public async Task Each_playlist_keeps_its_own_layout()
    {
        var client = await NewUserAsync();
        var videos = await NewPlaylistAsync(client, "Videos");
        var reading = await NewPlaylistAsync(client, "Reading");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{videos}/view", new
        {
            showUrls = false, showThumbnails = true, viewMode = "grid",
        })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync($"/api/v1/playlists/{reading}/view", new
        {
            showUrls = false, showThumbnails = true, viewMode = "table",
        })).EnsureSuccessStatusCode();

        var videosRead = await client.GetFromJsonAsync<PlaylistWithViewDto>($"/api/v1/playlists/{videos}");
        var readingRead = await client.GetFromJsonAsync<PlaylistWithViewDto>($"/api/v1/playlists/{reading}");

        Assert.Equal("grid", videosRead!.View!.ViewMode);
        Assert.Equal("table", readingRead!.View!.ViewMode);
    }

    [Fact]
    public async Task A_playlist_with_no_saved_layout_reports_none()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Default");

        (await client.PutAsJsonAsync($"/api/v1/playlists/{playlist}/view", new
        {
            sort = "position", showUrls = false, showThumbnails = true,
        })).EnsureSuccessStatusCode();

        var read = await client.GetFromJsonAsync<PlaylistWithViewDto>($"/api/v1/playlists/{playlist}");

        // Null rather than a guess, so the client applies its own default.
        Assert.Null(read!.View!.ViewMode);
    }
}
