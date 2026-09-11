using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Adult detection reads a self-declared meta tag, so it gets false positives — and one of those
/// used to hide a playlist from everyone, permanently, with no way to say otherwise.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NsfwOverrideTests(PostgresApiFactory factory)
{
    private record NsfwPlaylistDto(
        Guid Id, string Name, string Slug, string? Description, string Visibility,
        int ItemCount, string[] Tags, bool Nsfw, Guid? FolderId, string? FolderName, string? NsfwSetting);

    private record PagedSummariesDto(List<PublicSummaryDto> Items, string? NextCursor);

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

    /// <summary>Flags the playlist's link adult, as automatic detection would.</summary>
    private async Task<Guid> FlagLinkAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var link = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .Select(i => i.Link!).FirstAsync();
        link.Nsfw = true;
        await db.SaveChangesAsync();

        return link.Id;
    }

    private static async Task<NsfwPlaylistDto> GetAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<NsfwPlaylistDto>($"/api/v1/playlists/{playlistId}"))!;

    [Fact]
    public async Task A_playlist_is_adult_automatically_when_an_item_is()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Auto");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await FlagLinkAsync(playlist);

        var read = await GetAsync(client, playlist);

        Assert.True(read.Nsfw);
        Assert.Equal("Auto", read.NsfwSetting);
    }

    [Fact]
    public async Task An_owner_can_say_a_false_positive_is_not_adult()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Wrongly flagged");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await FlagLinkAsync(playlist);

        // Hidden from discovery while the automatic reading stands.
        var slug = (await GetAsync(client, playlist)).Slug;
        var before = await factory.CreateClient()
            .GetFromJsonAsync<PagedSummariesDto>($"/api/v1/public/users/{username}/playlists");
        Assert.DoesNotContain(before!.Items, p => p.Slug == slug);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "No" }))
            .EnsureSuccessStatusCode();

        var read = await GetAsync(client, playlist);
        Assert.False(read.Nsfw);
        Assert.Equal("No", read.NsfwSetting);

        // And visible again to everyone, without anyone having to opt in.
        var after = await factory.CreateClient()
            .GetFromJsonAsync<PagedSummariesDto>($"/api/v1/public/users/{username}/playlists");
        Assert.Contains(after!.Items, p => p.Slug == slug);
    }

    [Fact]
    public async Task An_owner_can_mark_a_playlist_adult_that_detection_missed()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Self declared");
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "Yes" }))
            .EnsureSuccessStatusCode();

        var read = await GetAsync(client, playlist);
        Assert.True(read.Nsfw);
        Assert.Equal("Yes", read.NsfwSetting);
    }

    [Fact]
    public async Task Setting_it_back_to_auto_restores_the_automatic_reading()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Back to auto");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await FlagLinkAsync(playlist);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "No" }))
            .EnsureSuccessStatusCode();
        Assert.False((await GetAsync(client, playlist)).Nsfw);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "Auto" }))
            .EnsureSuccessStatusCode();

        var read = await GetAsync(client, playlist);
        Assert.True(read.Nsfw);
        Assert.Equal("Auto", read.NsfwSetting);
    }

    [Fact]
    public async Task An_unrelated_update_leaves_the_setting_alone()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Untouched setting");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await FlagLinkAsync(playlist);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "No" }))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { name = "Renamed" }))
            .EnsureSuccessStatusCode();

        var read = await GetAsync(client, playlist);
        Assert.Equal("Renamed", read.Name);
        Assert.Equal("No", read.NsfwSetting);
        Assert.False(read.Nsfw);
    }

    [Fact]
    public async Task An_override_carries_into_the_public_read_and_the_feed()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Public override");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await FlagLinkAsync(playlist);

        var slug = (await GetAsync(client, playlist)).Slug;
        var anonymous = factory.CreateClient();

        // While flagged, both the page and the feed refuse an anonymous viewer.
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/api/v1/public/playlists/{username}/{slug}")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/api/v1/public/playlists/{username}/{slug}/feed.rss")).StatusCode);

        (await client.PatchAsJsonAsync($"/api/v1/playlists/{playlist}", new { nsfw = "No" }))
            .EnsureSuccessStatusCode();

        (await anonymous.GetAsync($"/api/v1/public/playlists/{username}/{slug}")).EnsureSuccessStatusCode();
        (await anonymous.GetAsync($"/api/v1/public/playlists/{username}/{slug}/feed.rss")).EnsureSuccessStatusCode();
    }
}
