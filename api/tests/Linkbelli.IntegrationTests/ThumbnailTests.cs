using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Thumbnails are served from here rather than hotlinked. Rendering the origin URL told every
/// site in a playlist the viewer's IP and what they were looking at.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ThumbnailTests(PostgresApiFactory factory)
{
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

    /// <summary>Points a seeded link's thumbnail somewhere, and returns the link's id.</summary>
    private async Task<Guid> SetThumbnailAsync(Guid playlistId, string? url)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var link = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .Select(i => i.Link!).FirstAsync();
        link.ThumbnailUrl = url;
        await db.SaveChangesAsync();

        return link.Id;
    }

    [Fact]
    public async Task A_link_with_no_thumbnail_is_not_found()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "No image");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await SetThumbnailAsync(playlist, null);

        var res = await factory.CreateClient().GetAsync($"/api/v1/thumbnails/{linkId}");

        // The page falls back to the site's favicon, exactly as for a link that never had one.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task An_unknown_link_is_not_found()
    {
        var res = await factory.CreateClient().GetAsync($"/api/v1/thumbnails/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_thumbnail_that_cannot_be_fetched_is_not_found_rather_than_an_error()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Unreachable");
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        // Blocked by the SSRF guard, which is the same protection enrichment gets: a thumbnail
        // URL is attacker-supplied in exactly the way a page URL is.
        var linkId = await SetThumbnailAsync(playlist, "http://localhost/secret.png");

        var res = await factory.CreateClient().GetAsync($"/api/v1/thumbnails/{linkId}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task The_endpoint_is_anonymous_because_public_playlists_show_thumbnails()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Public images");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = await SetThumbnailAsync(playlist, null);

        var anonymous = await factory.CreateClient().GetAsync($"/api/v1/thumbnails/{linkId}");

        // 404 for a link with no image, rather than 401 — it answered, it just had nothing.
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);
    }

    [Fact]
    public async Task The_item_list_still_reports_the_original_address()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Origin url");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await SetThumbnailAsync(playlist, "https://cdn.example/image.png");

        var res = await client.GetAsync($"/api/v1/playlists/{playlist}/items");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync();

        // The contract keeps saying where the image really is. Proxying is a rendering decision,
        // and a feed reader or an export wants the real address.
        Assert.Contains("https://cdn.example/image.png", body);
    }
}
