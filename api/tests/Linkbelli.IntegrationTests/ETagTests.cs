using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Everything that polls this API — the extension, the sync client, a feed reader — re-downloaded
/// an identical payload every time it looked. These cover the conditional GET that stops that.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ETagTests(PostgresApiFactory factory)
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

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? etag = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task A_read_comes_back_with_a_tag()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Tagged {Guid.NewGuid():N}");

        var res = await GetAsync(client, "/api/v1/playlists");
        res.EnsureSuccessStatusCode();

        Assert.NotNull(res.Headers.ETag);
        Assert.True(res.Headers.ETag!.IsWeak);
    }

    [Fact]
    public async Task Asking_again_with_the_tag_gets_nothing_back()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Unchanged {Guid.NewGuid():N}");

        var first = await GetAsync(client, "/api/v1/playlists");
        var etag = first.Headers.ETag!.ToString();

        var second = await GetAsync(client, "/api/v1/playlists", etag);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsStringAsync());
        // Still carried, so the client can keep using it next time.
        Assert.Equal(etag, second.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task A_change_produces_a_different_tag()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Before {Guid.NewGuid():N}");

        var first = await GetAsync(client, "/api/v1/playlists");
        var etag = first.Headers.ETag!.ToString();

        await NewPlaylistAsync(client, $"After {Guid.NewGuid():N}");

        var second = await GetAsync(client, "/api/v1/playlists", etag);

        // The tag is derived from the response, so it is right by construction rather than by
        // remembering to bump a version somewhere.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(etag, second.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task A_stale_tag_gets_the_body()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Stale {Guid.NewGuid():N}");

        var res = await GetAsync(client, "/api/v1/playlists", "W/\"0123456789abcdef0123456789abcdef\"");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.NotEmpty(await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task One_persons_tag_is_not_anothers()
    {
        var first = await NewUserAsync();
        await NewPlaylistAsync(first, $"Mine {Guid.NewGuid():N}");
        var mine = (await GetAsync(first, "/api/v1/playlists")).Headers.ETag!.ToString();

        var second = await NewUserAsync();
        await NewPlaylistAsync(second, $"Theirs {Guid.NewGuid():N}");

        // Different content, so a tag from one account can never suppress another's body.
        var theirs = await GetAsync(second, "/api/v1/playlists", mine);

        Assert.Equal(HttpStatusCode.OK, theirs.StatusCode);
    }

    [Fact]
    public async Task A_star_means_anything_you_have()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Star {Guid.NewGuid():N}");

        var res = await GetAsync(client, "/api/v1/playlists", "*");

        Assert.Equal(HttpStatusCode.NotModified, res.StatusCode);
    }

    [Fact]
    public async Task A_tag_that_lost_its_weakness_marker_is_still_recognised()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Stripped {Guid.NewGuid():N}");

        var first = await GetAsync(client, "/api/v1/playlists");
        var stripped = first.Headers.ETag!.ToString().Replace("W/", "");

        // A proxy that strips the marker is still talking about the same representation;
        // refusing to recognise it would just resend the body.
        var second = await GetAsync(client, "/api/v1/playlists", stripped);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task A_failure_is_never_tagged()
    {
        var client = await NewUserAsync();

        var res = await GetAsync(client, $"/api/v1/playlists/{Guid.NewGuid()}");

        // Caching a 404 under an entity tag is how a transient failure becomes a sticky one.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Null(res.Headers.ETag);
    }

    [Fact]
    public async Task Writes_are_left_alone()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Write {Guid.NewGuid():N}" });

        res.EnsureSuccessStatusCode();
        Assert.Null(res.Headers.ETag);
    }
}
