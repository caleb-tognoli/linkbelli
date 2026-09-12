using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What the API says when a request is the caller's fault.
/// </summary>
/// <remarks>
/// Two things used to come back 500 here, which is wrong on the wire and — more practically —
/// means every client mistake and every lost race shows up in whatever watches the error rate.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class MalformedRequestTests(PostgresApiFactory factory)
{
    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    /// <summary>
    /// A body the framework cannot bind raises BadHttpRequestException, which already carries a
    /// 400 — and was then reported as a 500 because nothing mapped it.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("\"a string, not an object\"")]
    public async Task An_unusable_body_is_the_callers_fault(string raw)
    {
        var client = factory.CreateClient();

        var res = await client.PostAsync("/api/v1/auth/login", Json(raw));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_unusable_body_is_the_callers_fault_on_authenticated_routes_too()
    {
        var client = await SignedInAsync();

        var res = await client.PostAsync("/api/v1/playlists", Json("null"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

/// <summary>
/// Two people naming a playlist the same thing at the same time.
/// </summary>
/// <remarks>
/// Slug selection reads "is this taken" and then writes, which is a check-then-act: both callers
/// saw it free and the loser hit the unique index and came back 500. Not exotic — it is exactly
/// what a double-clicked Create button, or an offline queue replaying, produces.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class PlaylistSlugRaceTests(PostgresApiFactory factory)
{
    private async Task<HttpClient> SignedInAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    [Fact]
    public async Task The_same_name_twice_in_a_row_gets_a_numbered_slug()
    {
        var client = await SignedInAsync();

        var first = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Reading" });
        var second = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Reading" });

        var a = await first.Content.ReadFromJsonAsync<PlaylistDto>();
        var b = await second.Content.ReadFromJsonAsync<PlaylistDto>();

        Assert.Equal("reading", a!.Slug);
        Assert.Equal("reading-2", b!.Slug);
    }

    [Fact]
    public async Task The_same_name_from_several_requests_at_once_never_answers_500()
    {
        var client = await SignedInAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                client.PostAsJsonAsync("/api/v1/playlists", new { name = "All At Once" })));

        // The point of the fix: a lost race is settled, or at worst reported as a conflict the
        // caller can act on. It is never a server error.
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, responses.Select(r => r.StatusCode));
        Assert.All(responses, r =>
            Assert.True(
                r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"unexpected {(int)r.StatusCode}"));
    }

    [Fact]
    public async Task Every_playlist_that_was_created_got_its_own_slug()
    {
        var client = await SignedInAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ =>
                client.PostAsJsonAsync("/api/v1/playlists", new { name = "Distinct Slugs" })));

        var slugs = new List<string>();
        foreach (var res in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
        {
            slugs.Add((await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Slug);
        }

        Assert.NotEmpty(slugs);
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
    }

    /// <summary>
    /// Different people may both have a "Reading" — the index is per owner, so this must not be
    /// mistaken for a collision and re-rolled into "reading-2".
    /// </summary>
    [Fact]
    public async Task Two_people_can_each_have_the_same_slug()
    {
        var alice = await SignedInAsync();
        var bob = await SignedInAsync();

        var hers = await alice.PostAsJsonAsync("/api/v1/playlists", new { name = "Shared Name" });
        var his = await bob.PostAsJsonAsync("/api/v1/playlists", new { name = "Shared Name" });

        Assert.Equal("shared-name", (await hers.Content.ReadFromJsonAsync<PlaylistDto>())!.Slug);
        Assert.Equal("shared-name", (await his.Content.ReadFromJsonAsync<PlaylistDto>())!.Slug);
    }
}
