using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Discovery ordered everything by age, which rewards being new rather than being good, and ended
/// at whatever you happened to open. These cover the orderings, the tags that are actually moving,
/// and the row that leads from one playlist to the next.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DiscoveryRankingTests(PostgresApiFactory factory)
{
    private record Row(string OwnerUsername, string Slug, string Name, int ItemCount, int LikeCount, DateTimeOffset? LastItemAt);

    private record Page(List<Row> Items);

    private record TagRow(string Name, int PlaylistCount);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<(Guid Id, string Slug)> NewPublicPlaylistAsync(
        HttpClient client, string name, string[]? tags = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new
        {
            name,
            visibility = "Public",
            tags = tags ?? [],
        });
        res.EnsureSuccessStatusCode();
        var created = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        return (created.Id, created.Slug);
    }

    private static async Task LikeAsync(HttpClient client, Guid playlistId) =>
        (await client.PostAsync($"/api/v1/playlists/{playlistId}/like", null)).EnsureSuccessStatusCode();

    /// <summary>Ages a playlist, so "newest first" can be told apart from every other ordering.</summary>
    private async Task BackdateAsync(Guid playlistId, TimeSpan by)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var playlist = await db.Playlists.FirstAsync(p => p.Id == playlistId);
        playlist.CreationTime = DateTimeOffset.UtcNow - by;
        await db.SaveChangesAsync();
    }

    private static async Task<Page> DiscoverAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Page>($"/api/v1/public/playlists?{query}"))!;

    [Fact]
    public async Task Newest_first_is_still_what_you_get_by_default()
    {
        var (client, _) = await NewUserAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (older, _) = await NewPublicPlaylistAsync(client, $"{tag} older");
        await BackdateAsync(older, TimeSpan.FromDays(30));
        await NewPublicPlaylistAsync(client, $"{tag} newer");

        var page = await DiscoverAsync(client, $"q={tag}");

        Assert.Equal($"{tag} newer", page.Items[0].Name);
    }

    [Fact]
    public async Task Most_liked_puts_the_best_first_however_old_it_is()
    {
        var (client, _) = await NewUserAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (loved, _) = await NewPublicPlaylistAsync(client, $"{tag} loved");
        await BackdateAsync(loved, TimeSpan.FromDays(365));
        await NewPublicPlaylistAsync(client, $"{tag} ignored");

        var (visitor, _) = await NewUserAsync();
        await LikeAsync(visitor, loved);

        var page = await DiscoverAsync(client, $"q={tag}&sort=liked");

        // A list posted last year that people keep coming back to was unfindable before this.
        Assert.Equal($"{tag} loved", page.Items[0].Name);
    }

    [Fact]
    public async Task Largest_puts_the_fullest_first()
    {
        var (client, _) = await NewUserAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (small, _) = await NewPublicPlaylistAsync(client, $"{tag} small");
        var (big, _) = await NewPublicPlaylistAsync(client, $"{tag} big");
        await factory.SeedEnrichedItemsAsync(small, 1);
        await factory.SeedEnrichedItemsAsync(big, 5);

        var page = await DiscoverAsync(client, $"q={tag}&sort=largest");

        Assert.Equal($"{tag} big", page.Items[0].Name);
        Assert.Equal(5, page.Items[0].ItemCount);
    }

    [Fact]
    public async Task Recently_active_is_about_the_links_not_the_playlist()
    {
        var (client, _) = await NewUserAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Old list, still being added to — exactly what "recently active" is for.
        var (tended, _) = await NewPublicPlaylistAsync(client, $"{tag} tended");
        await BackdateAsync(tended, TimeSpan.FromDays(200));
        await factory.SeedEnrichedItemsAsync(tended, 1);

        var (abandoned, _) = await NewPublicPlaylistAsync(client, $"{tag} abandoned");
        await BackdateAsync(abandoned, TimeSpan.FromDays(2));

        var page = await DiscoverAsync(client, $"q={tag}&sort=active");

        Assert.Equal($"{tag} tended", page.Items[0].Name);
        Assert.NotNull(page.Items[0].LastItemAt);
    }

    [Fact]
    public async Task An_ordering_nobody_asked_for_falls_back_to_newest()
    {
        var (client, _) = await NewUserAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (older, _) = await NewPublicPlaylistAsync(client, $"{tag} older");
        await BackdateAsync(older, TimeSpan.FromDays(10));
        await NewPublicPlaylistAsync(client, $"{tag} newer");

        // A typo shows the default rather than an empty page or an arbitrary order.
        var page = await DiscoverAsync(client, $"q={tag}&sort=populr");

        Assert.Equal($"{tag} newer", page.Items[0].Name);
    }

    [Fact]
    public async Task Similar_playlists_are_found_by_shared_links()
    {
        var (client, username) = await NewUserAsync();
        var url = $"https://similar.example/{Guid.NewGuid():N}";

        var (subject, slug) = await NewPublicPlaylistAsync(client, $"Subject {Guid.NewGuid():N}");
        var (sibling, _) = await NewPublicPlaylistAsync(client, $"Sibling {Guid.NewGuid():N}");
        await NewPublicPlaylistAsync(client, $"Unrelated {Guid.NewGuid():N}");

        await factory.SeedEnrichedItemsAsync(subject, 1, url: _ => url);
        await factory.SeedEnrichedItemsAsync(sibling, 1, url: _ => url);

        var similar = await client.GetFromJsonAsync<List<Row>>(
            $"/api/v1/public/playlists/{username}/{slug}/similar");

        // Two lists holding the same pages are about the same thing, whatever anyone tagged them.
        Assert.Contains(similar!, row => row.Slug != slug && row.Name.StartsWith("Sibling"));
        Assert.DoesNotContain(similar!, row => row.Name.StartsWith("Unrelated"));
    }

    [Fact]
    public async Task Similar_playlists_are_found_by_shared_tags()
    {
        var (client, username) = await NewUserAsync();
        var tag = $"t{Guid.NewGuid():N}"[..10];

        var (_, slug) = await NewPublicPlaylistAsync(client, $"Subject {Guid.NewGuid():N}", [tag]);
        await NewPublicPlaylistAsync(client, $"Sibling {Guid.NewGuid():N}", [tag]);

        var similar = await client.GetFromJsonAsync<List<Row>>(
            $"/api/v1/public/playlists/{username}/{slug}/similar");

        Assert.Contains(similar!, row => row.Name.StartsWith("Sibling"));
    }

    [Fact]
    public async Task A_playlist_with_nothing_in_common_gets_an_empty_row()
    {
        var (client, username) = await NewUserAsync();
        var (_, slug) = await NewPublicPlaylistAsync(client, $"Alone {Guid.NewGuid():N}");

        var similar = await client.GetFromJsonAsync<List<Row>>(
            $"/api/v1/public/playlists/{username}/{slug}/similar");

        // An empty row beats a row of arbitrary playlists dressed up as recommendations.
        Assert.Empty(similar!);
    }

    [Fact]
    public async Task A_playlist_is_never_similar_to_itself()
    {
        var (client, username) = await NewUserAsync();
        var tag = $"t{Guid.NewGuid():N}"[..10];
        var (_, slug) = await NewPublicPlaylistAsync(client, $"Subject {Guid.NewGuid():N}", [tag]);
        await NewPublicPlaylistAsync(client, $"Sibling {Guid.NewGuid():N}", [tag]);

        var similar = await client.GetFromJsonAsync<List<Row>>(
            $"/api/v1/public/playlists/{username}/{slug}/similar");

        Assert.DoesNotContain(similar!, row => row.Slug == slug);
    }

    [Fact]
    public async Task Trending_tags_are_the_ones_that_have_moved_lately()
    {
        var (client, _) = await NewUserAsync();
        var fresh = $"t{Guid.NewGuid():N}"[..10];
        var stale = $"t{Guid.NewGuid():N}"[..10];

        await NewPublicPlaylistAsync(client, $"Fresh {Guid.NewGuid():N}", [fresh]);
        var (old, _) = await NewPublicPlaylistAsync(client, $"Stale {Guid.NewGuid():N}", [stale]);
        await BackdateAsync(old, TimeSpan.FromDays(120));

        var trending = await client.GetFromJsonAsync<List<TagRow>>("/api/v1/public/tags/trending");

        // The all-time cloud is dominated by whatever was popular first and never changes.
        Assert.Contains(trending!, row => row.Name == fresh);
        Assert.DoesNotContain(trending!, row => row.Name == stale);
    }

    [Fact]
    public async Task A_dormant_tag_comes_back_when_someone_adds_to_the_list()
    {
        var (client, _) = await NewUserAsync();
        var tag = $"t{Guid.NewGuid():N}"[..10];

        var (playlist, _) = await NewPublicPlaylistAsync(client, $"Revived {Guid.NewGuid():N}", [tag]);
        await BackdateAsync(playlist, TimeSpan.FromDays(120));

        Assert.DoesNotContain(
            await client.GetFromJsonAsync<List<TagRow>>("/api/v1/public/tags/trending") ?? [],
            row => row.Name == tag);

        // An old list someone is still adding to is active, whatever its creation date says.
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        Assert.Contains(
            await client.GetFromJsonAsync<List<TagRow>>("/api/v1/public/tags/trending") ?? [],
            row => row.Name == tag);
    }
}
