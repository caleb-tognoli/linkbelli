using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A public playlist is readable as RSS, Atom and JSON Feed. Visibility has to hold here exactly
/// as it does for the HTML read — a feed URL is a public URL.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PlaylistFeedTests(PostgresApiFactory factory)
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

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
        var res = await client.PostAsJsonAsync("/api/v1/playlists",
            new { name, description = "Things worth a second look", visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private HttpClient Anonymous() => factory.CreateClient();

    [Fact]
    public async Task A_public_playlist_is_readable_as_rss()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Weekend reading", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3, title: n => $"Post {n}");

        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");
        res.EnsureSuccessStatusCode();
        Assert.Equal("application/rss+xml", res.Content.Headers.ContentType!.MediaType);

        var channel = XDocument.Parse(await res.Content.ReadAsStringAsync()).Root!.Element("channel")!;
        Assert.Equal("Weekend reading", channel.Element("title")!.Value);
        Assert.Equal("Things worth a second look", channel.Element("description")!.Value);

        var items = channel.Elements("item").ToList();
        Assert.Equal(3, items.Count);
        // Entries point at the link itself, not back at a Linkbelli page.
        Assert.All(items, i => Assert.StartsWith("https://seed.example/", i.Element("link")!.Value));
    }

    [Fact]
    public async Task The_same_playlist_is_readable_as_atom_and_json()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Multi format", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        var basePath = $"/api/v1/public/playlists/{username}/{playlist.Slug}/feed";

        var atom = await Anonymous().GetAsync($"{basePath}.atom");
        atom.EnsureSuccessStatusCode();
        Assert.Equal("application/atom+xml", atom.Content.Headers.ContentType!.MediaType);
        var atomDoc = XDocument.Parse(await atom.Content.ReadAsStringAsync());
        Assert.Equal(2, atomDoc.Root!.Elements(Atom + "entry").Count());
        Assert.Equal(username, atomDoc.Root.Element(Atom + "author")!.Element(Atom + "name")!.Value);

        var json = await Anonymous().GetAsync($"{basePath}.json");
        json.EnsureSuccessStatusCode();
        Assert.Equal("application/feed+json", json.Content.Headers.ContentType!.MediaType);
        using var jsonDoc = JsonDocument.Parse(await json.Content.ReadAsStringAsync());
        Assert.Equal("https://jsonfeed.org/version/1.1", jsonDoc.RootElement.GetProperty("version").GetString());
        Assert.Equal(2, jsonDoc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task An_unlisted_playlist_is_syndicated_because_the_link_holder_asked_for_it()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Unlisted feed", "Unlisted");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_private_playlist_has_no_feed_even_for_its_owner()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Private feed", "Private");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        var anonymous = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");
        Assert.Equal(HttpStatusCode.NotFound, anonymous.StatusCode);

        // Signed in as the owner changes nothing: the feed URL is a public URL.
        var asOwner = await client.GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");
        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);
    }

    [Fact]
    public async Task An_unknown_format_is_not_found()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Format check", "Public");

        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.html");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Nsfw_playlists_are_not_syndicated()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Adult feed", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var link = await db.PlaylistItems.Where(i => i.PlaylistId == playlist.Id)
                .Select(i => i.Link!).FirstAsync();
            link.Nsfw = true;
            await db.SaveChangesAsync();
        }

        // A feed reader carries no session, so there is no viewer to have opted in.
        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Entries_are_newest_first_regardless_of_playlist_order()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Stream order", "Public");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3, title: n => $"Post {n}");

        // Stagger creation so "newest first" has something to sort on.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var items = await db.PlaylistItems.Where(i => i.PlaylistId == playlist.Id)
                .OrderBy(i => i.Position).ToListAsync();
            for (var n = 0; n < items.Count; n++)
            {
                items[n].CreationTime = DateTimeOffset.UtcNow.AddMinutes(-10 + n);
            }
            await db.SaveChangesAsync();
        }

        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");
        res.EnsureSuccessStatusCode();

        var titles = XDocument.Parse(await res.Content.ReadAsStringAsync())
            .Root!.Element("channel")!.Elements("item")
            .Select(i => i.Element("title")!.Value)
            .ToList();

        Assert.Equal(["Post 2", "Post 1", "Post 0"], titles);
    }

    [Fact]
    public async Task An_empty_public_playlist_still_serves_a_feed()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Nothing yet", "Public");

        var res = await Anonymous().GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}/feed.rss");
        res.EnsureSuccessStatusCode();

        var channel = XDocument.Parse(await res.Content.ReadAsStringAsync()).Root!.Element("channel")!;
        Assert.Empty(channel.Elements("item"));
        Assert.NotNull(channel.Element("lastBuildDate"));
    }
}
