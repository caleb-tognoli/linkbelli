using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A scripted client whose request times out cannot tell "never arrived" from "arrived, and the
/// reply was lost". Retrying was a coin flip between a duplicate and a missing row. These cover
/// the header that settles it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class IdempotencyTests(PostgresApiFactory factory)
{
    private record PagedPlaylists(List<PlaylistDto> Items);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage NewPlaylistRequest(string name, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/playlists")
        {
            Content = JsonContent.Create(new { name }),
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_first_answer()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();
        var name = $"Once only {Guid.NewGuid():N}";

        var first = await client.SendAsync(NewPlaylistRequest(name, key));
        first.EnsureSuccessStatusCode();
        var created = (await first.Content.ReadFromJsonAsync<PlaylistDto>())!;

        var second = await client.SendAsync(NewPlaylistRequest(name, key));
        second.EnsureSuccessStatusCode();
        var replayed = (await second.Content.ReadFromJsonAsync<PlaylistDto>())!;

        Assert.Equal(created.Id, replayed.Id);
        Assert.Equal(first.StatusCode, second.StatusCode);
        // Says plainly that nothing happened the second time.
        Assert.Equal("true", second.Headers.TryGetValues("Idempotent-Replay", out var values)
            ? values.First()
            : null);
    }

    [Fact]
    public async Task The_work_is_only_done_once()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();
        var name = $"Exactly one {Guid.NewGuid():N}";

        await client.SendAsync(NewPlaylistRequest(name, key));
        await client.SendAsync(NewPlaylistRequest(name, key));
        await client.SendAsync(NewPlaylistRequest(name, key));

        var mine = await client.GetFromJsonAsync<PagedPlaylists>($"/api/v1/playlists?q={Uri.EscapeDataString(name)}");

        Assert.Single(mine!.Items);
    }

    [Fact]
    public async Task Without_a_key_nothing_changes()
    {
        var client = await NewUserAsync();
        var name = $"Twice over {Guid.NewGuid():N}";

        await client.SendAsync(NewPlaylistRequest(name, key: null));
        await client.SendAsync(NewPlaylistRequest(name, key: null));

        // The header is opt-in. Requests without one behave exactly as they always did.
        var mine = await client.GetFromJsonAsync<PagedPlaylists>($"/api/v1/playlists?q={Uri.EscapeDataString(name)}");
        Assert.Equal(2, mine!.Items.Count);
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_refused()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();

        (await client.SendAsync(NewPlaylistRequest($"First {Guid.NewGuid():N}", key))).EnsureSuccessStatusCode();

        var second = await client.SendAsync(NewPlaylistRequest($"Different {Guid.NewGuid():N}", key));

        // Replaying the first answer would hide a client bug and silently drop this request.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Keys_are_scoped_to_the_caller()
    {
        var key = Guid.NewGuid().ToString();

        var first = await NewUserAsync();
        (await first.SendAsync(NewPlaylistRequest($"Mine {Guid.NewGuid():N}", key))).EnsureSuccessStatusCode();

        var second = await NewUserAsync();
        var theirs = await second.SendAsync(NewPlaylistRequest($"Theirs {Guid.NewGuid():N}", key));

        // Two clients picking the same UUID must not collide.
        theirs.EnsureSuccessStatusCode();
        Assert.False(theirs.Headers.Contains("Idempotent-Replay"));
    }

    [Fact]
    public async Task An_empty_key_is_rejected_rather_than_ignored()
    {
        var client = await NewUserAsync();

        // Whitespace rather than "": an empty header value is dropped by the client before it
        // is ever sent, so it cannot be what the server is asked about.
        var res = await client.SendAsync(NewPlaylistRequest($"Blank {Guid.NewGuid():N}", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_request_that_never_completed_gives_its_key_back()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();

        // An empty name is refused by the service, which throws rather than returning a result.
        var first = await client.SendAsync(NewPlaylistRequest("", key));
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);

        var second = await client.SendAsync(NewPlaylistRequest("", key));

        // Nothing was completed, so the key is not burned: the retry runs again and gets the same
        // refusal, rather than being told forever that the first attempt is still in flight.
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.False(second.Headers.Contains("Idempotent-Replay"));
    }

    [Fact]
    public async Task A_key_released_by_a_failure_can_carry_a_corrected_request()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.SendAsync(NewPlaylistRequest("", key))).StatusCode);

        // The point of giving the key back: a client that fixes its request and retries with the
        // same key should succeed, not be stuck behind a request that never happened.
        var corrected = await client.SendAsync(NewPlaylistRequest($"Fixed {Guid.NewGuid():N}", key));

        corrected.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task The_replayed_body_is_the_original_one()
    {
        var client = await NewUserAsync();
        var key = Guid.NewGuid().ToString();
        var name = $"Verbatim {Guid.NewGuid():N}";

        var first = await client.SendAsync(NewPlaylistRequest(name, key));
        var firstBody = await first.Content.ReadAsStringAsync();

        var second = await client.SendAsync(NewPlaylistRequest(name, key));
        var secondBody = await second.Content.ReadAsStringAsync();

        Assert.Equal(firstBody, secondBody);
    }
}
