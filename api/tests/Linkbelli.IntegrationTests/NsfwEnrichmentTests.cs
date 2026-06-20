using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class NsfwEnrichmentTests(PostgresApiFactory factory)
{
    private record MeDto(Guid UserId, bool ShowNsfw);

    private record ItemLinkDto(string? Title, bool Enriched, bool Nsfw);

    private record ItemDto(Guid Id, ItemLinkDto Link);

    private async Task<(HttpClient client, Guid userId)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await client.GetFromJsonAsync<MeDto>("/api/v1/me");
        return (client, me!.UserId);
    }

    private record Seeded(Guid Id, string Slug);

    /// <summary>Seeds a public playlist owned by <paramref name="userId"/> with the given links.</summary>
    private Seeded SeedPlaylist(Guid userId, string name, params (string suffix, bool enriched, bool nsfw)[] links)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var marker = Guid.NewGuid().ToString("N")[..10];

        var host = new Host { Hostname = $"h{marker}.test" };
        db.Hosts.Add(host);

        var playlist = new Playlist
        {
            OwnerId = userId,
            Name = name,
            Slug = $"pl-{marker}",
            Visibility = PlaylistVisibility.Public
        };
        db.Playlists.Add(playlist);

        long pos = 1024;
        foreach (var (suffix, enriched, nsfw) in links)
        {
            var link = new Link
            {
                CanonicalUrl = $"https://{host.Hostname}/{suffix}",
                UrlHash = $"{marker}-{suffix}",
                HostId = host.Id,
                Host = host,
                Title = suffix,
                EnrichedAt = enriched ? DateTimeOffset.UtcNow : null,
                Nsfw = nsfw
            };
            db.Links.Add(link);
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlist.Id,
                LinkId = link.Id,
                Position = pos,
                Status = PlaylistItemStatus.Added
            });
            pos += 1024;
        }

        db.SaveChanges();
        return new Seeded(playlist.Id, playlist.Slug);
    }

    [Fact]
    public async Task Items_show_only_enriched_and_respect_nsfw_preference()
    {
        var (client, userId) = await NewUserAsync();
        var seeded = SeedPlaylist(userId, "Mixed",
            ("clean", true, false),
            ("adult", true, true),
            ("pending", false, false));
        var playlistId = seeded.Id;

        // Default (ShowNsfw=false): only the enriched, non-NSFW item.
        var page1 = await client.GetFromJsonAsync<PagedDto<ItemDto>>($"/api/v1/playlists/{playlistId}/items");
        Assert.Single(page1!.Items);
        Assert.Equal("clean", page1.Items[0].Link.Title);

        // Opt in → both enriched items (the unenriched one stays hidden).
        var pref = await client.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true });
        Assert.Equal(HttpStatusCode.NoContent, pref.StatusCode);
        var page2 = await client.GetFromJsonAsync<PagedDto<ItemDto>>($"/api/v1/playlists/{playlistId}/items");
        Assert.Equal(2, page2!.Items.Count);
    }

    [Fact]
    public async Task Nsfw_playlist_hidden_from_listings_until_opted_in()
    {
        var (client, userId) = await NewUserAsync();
        var seeded = SeedPlaylist(userId, "Secret", ("x", true, true));

        var before = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>("/api/v1/playlists");
        Assert.DoesNotContain(before!.Items, p => p.Id == seeded.Id);

        // Anonymous discovery never shows the NSFW playlist.
        var anon = factory.CreateClient();
        var discover = await anon.GetFromJsonAsync<PagedDto<PublicSummaryDto>>("/api/v1/public/playlists?limit=100");
        Assert.DoesNotContain(discover!.Items, p => p.Slug == seeded.Slug);

        await client.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true });
        var after = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>("/api/v1/playlists");
        Assert.Contains(after!.Items, p => p.Id == seeded.Id);
    }
}
