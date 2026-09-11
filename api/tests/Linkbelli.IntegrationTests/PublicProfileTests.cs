using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Every discovery result names its owner, and until now there was nowhere to click through to.
/// A profile shows only what the owner published — and nothing about them personally.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PublicProfileTests(PostgresApiFactory factory)
{
    private record ProfileDto(string Username, DateTimeOffset JoinedAt, int PublicPlaylistCount, int PublicItemCount);
    private record PagedSummariesDto(List<PublicSummaryDto> Items, string? NextCursor);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(HttpClient client, string name, string visibility)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private HttpClient Anonymous() => factory.CreateClient();

    [Fact]
    public async Task A_profile_counts_only_what_the_owner_published()
    {
        var (client, username) = await NewUserAsync();

        var published = await NewPlaylistAsync(client, "Published", "Public");
        var unlisted = await NewPlaylistAsync(client, "Unlisted", "Unlisted");
        var secret = await NewPlaylistAsync(client, "Secret", "Private");

        await factory.SeedEnrichedItemsAsync(published.Id, 3);
        await factory.SeedEnrichedItemsAsync(unlisted.Id, 5);
        await factory.SeedEnrichedItemsAsync(secret.Id, 7);

        var res = await Anonymous().GetAsync($"/api/v1/public/users/{username}");
        res.EnsureSuccessStatusCode();
        var profile = (await res.Content.ReadFromJsonAsync<ProfileDto>())!;

        Assert.Equal(username, profile.Username);
        Assert.Equal(1, profile.PublicPlaylistCount);
        Assert.Equal(3, profile.PublicItemCount);
    }

    [Fact]
    public async Task A_profile_never_leaks_the_email()
    {
        var (_, username) = await NewUserAsync();

        var body = await (await Anonymous().GetAsync($"/api/v1/public/users/{username}")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("@example.com", body);
    }

    [Fact]
    public async Task The_joined_date_is_stamped_at_registration()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var (_, username) = await NewUserAsync();

        var res = await Anonymous().GetAsync($"/api/v1/public/users/{username}");
        res.EnsureSuccessStatusCode();
        var profile = (await res.Content.ReadFromJsonAsync<ProfileDto>())!;

        Assert.InRange(profile.JoinedAt, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Listing_a_profile_returns_public_playlists_only()
    {
        var (client, username) = await NewUserAsync();

        await NewPlaylistAsync(client, "On the profile", "Public");
        await NewPlaylistAsync(client, "Share by link", "Unlisted");
        await NewPlaylistAsync(client, "Mine alone", "Private");

        var res = await Anonymous().GetAsync($"/api/v1/public/users/{username}/playlists");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<PagedSummariesDto>())!;

        var only = Assert.Single(page.Items);
        Assert.Equal("On the profile", only.Name);
        Assert.Equal(username, only.OwnerUsername);
    }

    [Fact]
    public async Task An_unknown_username_is_not_found()
    {
        var res = await Anonymous().GetAsync("/api/v1/public/users/nobody-by-that-name");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_username_matches_regardless_of_case()
    {
        var (_, username) = await NewUserAsync();

        var res = await Anonymous().GetAsync($"/api/v1/public/users/{username.ToUpperInvariant()}");

        res.EnsureSuccessStatusCode();
        var profile = (await res.Content.ReadFromJsonAsync<ProfileDto>())!;
        Assert.Equal(username, profile.Username);
    }

    [Fact]
    public async Task An_nsfw_playlist_is_hidden_from_a_profile_unless_the_viewer_opted_in()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Adult list", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var link = await db.PlaylistItems.Where(i => i.PlaylistId == playlist.Id)
                .Select(i => i.Link!).FirstAsync();
            link.Nsfw = true;
            await db.SaveChangesAsync();
        }

        var res = await Anonymous().GetAsync($"/api/v1/public/users/{username}");
        res.EnsureSuccessStatusCode();
        var profile = (await res.Content.ReadFromJsonAsync<ProfileDto>())!;

        Assert.Equal(0, profile.PublicPlaylistCount);

        var listing = await Anonymous().GetAsync($"/api/v1/public/users/{username}/playlists");
        listing.EnsureSuccessStatusCode();
        Assert.Empty((await listing.Content.ReadFromJsonAsync<PagedSummariesDto>())!.Items);
    }
}
