using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Items are only listed once enrichment has finished with them, so a source run that adds 80
/// links makes a playlist fill in over minutes with a count that creeps upward on its own.
/// Reporting what is still coming is what makes that legible rather than mysterious.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PendingItemsTests(PostgresApiFactory factory)
{
    private record CountsDto(Guid Id, string Name, int ItemCount, int? PendingCount);
    private record PagedPlaylistsDto(List<CountsDto> Items, string? NextCursor);

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

    /// <summary>Adds items whose links have not been enriched — exactly what a source run leaves behind.</summary>
    private async Task SeedPendingAsync(Guid playlistId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname == "pending.example");
        if (host is null)
        {
            host = new Host { Hostname = "pending.example" };
            db.Hosts.Add(host);
            await db.SaveChangesAsync();
        }

        var tag = Guid.NewGuid().ToString("N")[..8];
        var position = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position) ?? 0;

        for (var n = 0; n < count; n++)
        {
            UrlCanonicalizer.TryCanonicalize($"https://pending.example/{tag}/{n}", out var canonical);
            var link = new Link
            {
                CanonicalUrl = canonical.Url,
                UrlHash = canonical.Hash,
                HostId = host.Id,
                // Deliberately not stamped: this is a link enrichment has not finished with.
                EnrichedAt = null,
            };
            db.Links.Add(link);

            position += PlaylistItem.PositionGap;
            db.PlaylistItems.Add(new PlaylistItem { PlaylistId = playlistId, Link = link, Position = position });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_playlist_reports_what_is_still_arriving()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Filling up");
        await factory.SeedEnrichedItemsAsync(playlist, 2);
        await SeedPendingAsync(playlist, 5);

        var read = await client.GetFromJsonAsync<CountsDto>($"/api/v1/playlists/{playlist}");

        Assert.Equal(2, read!.ItemCount);
        Assert.Equal(5, read.PendingCount);
    }

    [Fact]
    public async Task A_settled_playlist_reports_nothing_pending()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "All arrived");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        var read = await client.GetFromJsonAsync<CountsDto>($"/api/v1/playlists/{playlist}");

        Assert.Equal(3, read!.ItemCount);
        Assert.Equal(0, read.PendingCount);
    }

    [Fact]
    public async Task The_playlist_list_reports_it_too()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Listed while filling");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await SeedPendingAsync(playlist, 4);

        var page = await client.GetFromJsonAsync<PagedPlaylistsDto>("/api/v1/playlists");

        var listed = Assert.Single(page!.Items, p => p.Id == playlist);
        Assert.Equal(1, listed.ItemCount);
        Assert.Equal(4, listed.PendingCount);
    }

    [Fact]
    public async Task Pending_items_are_still_not_listed_as_items()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Not yet shown");
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        await SeedPendingAsync(playlist, 3);

        var items = await client.GetFromJsonAsync<PagedDto>($"/api/v1/playlists/{playlist}/items");

        // Showing three untitled rows would be worse than saying three are on the way.
        Assert.Single(items!.Items);
    }

    private record PagedDto(List<ItemDto> Items, string? NextCursor, int? Total);
}
