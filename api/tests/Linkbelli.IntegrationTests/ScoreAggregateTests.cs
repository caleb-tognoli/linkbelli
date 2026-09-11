using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Scores fed exactly one thing — sorting inside a single playlist. These cover what they feed
/// now: an average worth reading, and a best-rated view that spans playlists.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ScoreAggregateTests(PostgresApiFactory factory)
{
    private record ScoredPlaylistDto(Guid Id, string Name, int ItemCount, double? AverageScore, int? ScoredCount);
    private record HitDto(Guid ItemId, string PlaylistName, int? Score);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);

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

    private static async Task ScoreAsync(HttpClient client, Guid itemId, int score) =>
        (await client.PutAsJsonAsync($"/api/v1/items/{itemId}/score", new { score })).EnsureSuccessStatusCode();

    [Fact]
    public async Task A_playlist_reports_the_average_of_what_was_rated()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Rated");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        await ScoreAsync(client, seeded[0], 60);
        await ScoreAsync(client, seeded[1], 80);
        // Two left unrated on purpose.

        var read = await client.GetFromJsonAsync<ScoredPlaylistDto>($"/api/v1/playlists/{playlist}");

        // Averaged over the rated items only — counting the unrated as zero would report 35 and
        // say something false about the playlist.
        Assert.Equal(70d, read!.AverageScore);
        Assert.Equal(2, read.ScoredCount);
    }

    [Fact]
    public async Task A_playlist_with_nothing_rated_has_no_average()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Unrated");
        await factory.SeedEnrichedItemsAsync(playlist, 3);

        var read = await client.GetFromJsonAsync<ScoredPlaylistDto>($"/api/v1/playlists/{playlist}");

        Assert.Null(read!.AverageScore);
        Assert.Equal(0, read.ScoredCount);
    }

    [Fact]
    public async Task Search_can_rank_the_best_rated_across_playlists()
    {
        var client = await NewUserAsync();
        var reading = await NewPlaylistAsync(client, "Reading");
        var watching = await NewPlaylistAsync(client, "Watching");

        var inReading = await factory.SeedEnrichedItemsAsync(reading, 2);
        var inWatching = await factory.SeedEnrichedItemsAsync(watching, 1);

        await ScoreAsync(client, inReading[0], 40);
        await ScoreAsync(client, inWatching[0], 95);
        await ScoreAsync(client, inReading[1], 70);

        var res = await client.GetAsync("/api/v1/search?sort=score&limit=10");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        Assert.Equal([95, 70, 40], page.Items.Select(h => h.Score).ToArray());
        Assert.Equal("Watching", page.Items[0].PlaylistName);
    }

    [Fact]
    public async Task Unrated_items_sort_last_rather_than_as_zero()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mixed");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        await ScoreAsync(client, seeded[0], 10);

        var res = await client.GetAsync("/api/v1/search?sort=score&limit=10");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        // "Not rated" is not the same as "rated badly".
        Assert.Equal(10, page.Items[0].Score);
        Assert.All(page.Items.Skip(1), h => Assert.Null(h.Score));
    }
}
