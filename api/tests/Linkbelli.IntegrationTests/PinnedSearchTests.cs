using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Keeping a saved search where you can see it.
/// </summary>
/// <remarks>
/// A saved search was a question you re-asked by hand from the search page, so "everything unread
/// from these five sites under ten minutes" could be asked but not <em>had</em> — not opened from
/// the sidebar, not glanced at, not a thing with a number beside it.
///
/// The full version is a playlist whose membership is a query, which brings a pile of decisions
/// with it: manual ordering, a cover, membership roles, none of which a query can have. This is
/// most of the value for a fraction of that.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class PinnedSearchTests(PostgresApiFactory factory)
{
    private record SavedDto(Guid Id, string Name, bool Pinned);

    private record PinnedDto(Guid Id, string Name, int Count);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<SavedDto> SaveAsync(HttpClient client, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        foreach (var property in (extra ?? new { }).GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(extra);
        }

        var res = await client.PostAsJsonAsync("/api/v1/search/saved", body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SavedDto>())!;
    }

    private static Task<HttpResponseMessage> PinAsync(HttpClient client, Guid id, bool pinned) =>
        client.PutAsJsonAsync($"/api/v1/search/saved/{id}/pinned", new { pinned });

    private static async Task<List<PinnedDto>> PinnedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<PinnedDto>>("/api/v1/search/saved/pinned"))!;

    [Fact]
    public async Task A_saved_search_starts_unpinned()
    {
        var client = await NewUserAsync();

        var saved = await SaveAsync(client, $"Unread {Guid.NewGuid():N}");

        Assert.False(saved.Pinned);
        Assert.Empty(await PinnedAsync(client));
    }

    /// <summary>The count is the point. Without one it is a link; with one it is something you glance at.</summary>
    [Fact]
    public async Task A_pinned_search_comes_back_with_what_matches_right_now()
    {
        var client = await NewUserAsync();
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Counted {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 3, title: n => $"{marker} {n}");

        var saved = await SaveAsync(client, $"Marked {Guid.NewGuid():N}", new { q = marker });
        (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();

        var pinned = Assert.Single(await PinnedAsync(client), p => p.Id == saved.Id);
        Assert.Equal(3, pinned.Count);
    }

    /// <summary>
    /// The count moves with the library, which is what saving the question rather than the answer
    /// was for.
    /// </summary>
    [Fact]
    public async Task The_count_follows_what_is_there()
    {
        var client = await NewUserAsync();
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Growing {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        var marker = "marker" + Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 1, title: n => $"{marker} {n}");

        var saved = await SaveAsync(client, $"Following {Guid.NewGuid():N}", new { q = marker });
        (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();

        Assert.Equal(1, Assert.Single(await PinnedAsync(client), p => p.Id == saved.Id).Count);

        await factory.SeedEnrichedItemsAsync(playlist, 2, title: n => $"{marker} more {n}");

        Assert.Equal(3, Assert.Single(await PinnedAsync(client), p => p.Id == saved.Id).Count);
    }

    [Fact]
    public async Task It_can_be_taken_out_again()
    {
        var client = await NewUserAsync();
        var saved = await SaveAsync(client, $"In and out {Guid.NewGuid():N}");

        (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();
        Assert.Contains(await PinnedAsync(client), p => p.Id == saved.Id);

        (await PinAsync(client, saved.Id, false)).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await PinnedAsync(client), p => p.Id == saved.Id);
    }

    /// <summary>
    /// A budget rather than a taste: each pinned search is a count query on a request the app
    /// layout makes on every navigation.
    /// </summary>
    [Fact]
    public async Task There_is_a_limit_and_it_says_so()
    {
        var client = await NewUserAsync();

        for (var i = 0; i < 5; i++)
        {
            var saved = await SaveAsync(client, $"Pinned {i} {Guid.NewGuid():N}");
            (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();
        }

        var sixth = await SaveAsync(client, $"One too many {Guid.NewGuid():N}");
        var res = await PinAsync(client, sixth.Id, true);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("5", await res.Content.ReadAsStringAsync());
    }

    /// <summary>Pinning something already pinned is not another one against the limit.</summary>
    [Fact]
    public async Task Pinning_the_same_one_twice_does_not_use_up_the_budget()
    {
        var client = await NewUserAsync();
        var saved = await SaveAsync(client, $"Twice {Guid.NewGuid():N}");

        (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();
        (await PinAsync(client, saved.Id, true)).EnsureSuccessStatusCode();

        Assert.Single(await PinnedAsync(client), p => p.Id == saved.Id);
    }

    [Fact]
    public async Task Somebody_elses_saved_search_is_not_yours_to_pin()
    {
        var mine = await NewUserAsync();
        var theirs = await NewUserAsync();
        var saved = await SaveAsync(theirs, $"Theirs {Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, (await PinAsync(mine, saved.Id, true)).StatusCode);
    }
}
