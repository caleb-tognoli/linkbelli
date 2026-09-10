using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Covers the playlist-level dedup rules in ImportService. Row counts are read straight from the
/// database rather than the API: the item endpoints only surface links that enrichment has already
/// processed, so a freshly imported item is real but not yet visible.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ImportDedupTests(PostgresApiFactory factory)
{
    private record ImportResultDto(int Imported, int Skipped, List<string> Errors);

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

    private static async Task<ImportResultDto> ImportAsync(HttpClient client, Guid playlistId, params string[] urls)
    {
        var res = await client.PostAsJsonAsync("/api/v1/import", new
        {
            rows = urls.Select(u => new { url = u, note = (string?)null }).ToArray(),
            playlistId,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<ImportResultDto>())!;
    }

    /// <summary>Actual persisted item rows, bypassing the "enriched only" read filters.</summary>
    private async Task<int> RowCountAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        return await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlistId);
    }

    [Fact]
    public async Task Re_importing_the_same_rows_skips_them_instead_of_duplicating()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Import target");

        var tag = Guid.NewGuid().ToString("N")[..8];
        string[] urls =
        [
            $"https://example.com/{tag}/a",
            $"https://example.com/{tag}/b",
            $"https://example.com/{tag}/c",
        ];

        var first = await ImportAsync(client, playlist, urls);
        Assert.Empty(first.Errors);
        Assert.Equal(3, first.Imported);
        Assert.Equal(0, first.Skipped);
        Assert.Equal(3, await RowCountAsync(playlist));

        // Same file again: every link is already present, so nothing is added.
        var second = await ImportAsync(client, playlist, urls);
        Assert.Equal(0, second.Imported);
        Assert.Equal(3, second.Skipped);
        Assert.Equal(3, await RowCountAsync(playlist));
    }

    [Fact]
    public async Task Partial_overlap_adds_only_the_new_rows()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Partial overlap");

        var tag = Guid.NewGuid().ToString("N")[..8];
        await ImportAsync(client, playlist, $"https://example.com/{tag}/a", $"https://example.com/{tag}/b");

        var second = await ImportAsync(client, playlist,
            $"https://example.com/{tag}/b", $"https://example.com/{tag}/c");

        Assert.Equal(1, second.Imported); // only /c is new
        Assert.Equal(1, second.Skipped);  // /b was already there
        Assert.Equal(3, await RowCountAsync(playlist));
    }

    [Fact]
    public async Task Duplicate_rows_within_one_file_collapse_to_a_single_item()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Within-file dupes");

        var url = $"https://example.com/{Guid.NewGuid():N}/same";
        var result = await ImportAsync(client, playlist, url, url, url);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, await RowCountAsync(playlist));
    }

    [Fact]
    public async Task Urls_differing_only_by_tracking_params_or_fragment_dedup_together()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Canonical dupes");

        var tag = Guid.NewGuid().ToString("N")[..8];
        await ImportAsync(client, playlist, $"https://example.com/{tag}/x");

        var second = await ImportAsync(client, playlist,
            $"https://example.com/{tag}/x?utm_source=newsletter",
            $"https://example.com/{tag}/x#section");

        Assert.Equal(0, second.Imported);
        // Skipped counts distinct links, not rows: both spellings collapse to one link, and the
        // second occurrence is dropped by the within-file guard before it can be counted again.
        Assert.Equal(1, second.Skipped);
        Assert.Equal(1, await RowCountAsync(playlist));
    }

    [Fact]
    public async Task Importing_the_same_links_into_a_different_playlist_still_adds_them()
    {
        var client = await NewUserAsync();
        var first = await NewPlaylistAsync(client, "First list");
        var second = await NewPlaylistAsync(client, "Second list");

        var tag = Guid.NewGuid().ToString("N")[..8];
        string[] urls = [$"https://example.com/{tag}/p", $"https://example.com/{tag}/q"];

        await ImportAsync(client, first, urls);
        var into2 = await ImportAsync(client, second, urls);

        // Dedup is per playlist, not global: the links already exist but this playlist is empty.
        Assert.Equal(2, into2.Imported);
        Assert.Equal(0, into2.Skipped);
        Assert.Equal(2, await RowCountAsync(second));
    }
}
