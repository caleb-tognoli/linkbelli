using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AuthFlowTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Register_login_then_create_and_list_playlist()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "My List", visibility = "Private" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<PlaylistDto>();
        Assert.Equal("My List", created!.Name);

        var page = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>("/api/v1/playlists");
        Assert.Contains(page!.Items, p => p.Id == created.Id);
    }

    [Fact]
    public async Task Protected_endpoint_without_auth_is_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/playlists");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

[Collection(IntegrationCollection.Name)]
public class ScopeEnforcementTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Scoped_key_allows_listed_scope_and_forbids_others()
    {
        var bearer = factory.CreateClient();
        var token = await bearer.RegisterAndLoginAsync(NewUsername());
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var keyResp = await bearer.PostAsJsonAsync("/api/v1/me/apikeys",
            new { name = "read-only", scopes = new[] { "playlists:read" }, expiresAt = (DateTimeOffset?)null });
        var key = await keyResp.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var keyed = factory.CreateClient();
        keyed.DefaultRequestHeaders.Add("X-Api-Key", key!.Token);

        var read = await keyed.GetAsync("/api/v1/playlists");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await keyed.PostAsJsonAsync("/api/v1/playlists", new { name = "nope" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Unscoped_key_has_full_access()
    {
        var bearer = factory.CreateClient();
        var token = await bearer.RegisterAndLoginAsync(NewUsername());
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var keyResp = await bearer.PostAsJsonAsync("/api/v1/me/apikeys",
            new { name = "full", scopes = Array.Empty<string>(), expiresAt = (DateTimeOffset?)null });
        var key = await keyResp.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var keyed = factory.CreateClient();
        keyed.DefaultRequestHeaders.Add("X-Api-Key", key!.Token);

        var write = await keyed.PostAsJsonAsync("/api/v1/playlists", new { name = "made by key" });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
    }
}

[Collection(IntegrationCollection.Name)]
public class PublicPlaylistTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Public_playlist_is_readable_anonymously_by_username_and_slug()
    {
        var username = NewUsername();
        var owner = factory.CreateClient();
        var token = await owner.RegisterAndLoginAsync(username);
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await owner.PostAsJsonAsync("/api/v1/playlists", new { name = "Shared", visibility = "Public" });
        var playlist = await create.Content.ReadFromJsonAsync<PlaylistDto>();

        var addItem = await owner.PostAsJsonAsync($"/api/v1/playlists/{playlist!.Id}/items",
            new { url = "https://example.com/article" });
        Assert.Equal(HttpStatusCode.Created, addItem.StatusCode);

        var anon = factory.CreateClient();
        var read = await anon.GetAsync($"/api/v1/public/playlists/{username}/{playlist.Slug}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var seen = await read.Content.ReadFromJsonAsync<PlaylistDto>();
        Assert.Equal("Shared", seen!.Name);

        var items = await anon.GetFromJsonAsync<PagedDto<ItemDto>>(
            $"/api/v1/public/playlists/{username}/{playlist.Slug}/items");
        Assert.Single(items!.Items);
    }

    [Fact]
    public async Task Private_playlist_is_not_visible_anonymously()
    {
        var username = NewUsername();
        var owner = factory.CreateClient();
        var token = await owner.RegisterAndLoginAsync(username);
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var create = await owner.PostAsJsonAsync("/api/v1/playlists", new { name = "Secret", visibility = "Private" });
        var playlist = await create.Content.ReadFromJsonAsync<PlaylistDto>();

        var anon = factory.CreateClient();
        var read = await anon.GetAsync($"/api/v1/public/playlists/{username}/{playlist!.Slug}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }
}

[Collection(IntegrationCollection.Name)]
public class RateLimitTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Exceeding_the_bucket_returns_429_with_retry_after()
    {
        var bearer = factory.CreateClient();
        var token = await bearer.RegisterAndLoginAsync(NewUsername());
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var keyResp = await bearer.PostAsJsonAsync("/api/v1/me/apikeys",
            new { name = "burst", scopes = Array.Empty<string>(), expiresAt = (DateTimeOffset?)null });
        var key = await keyResp.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        // A dedicated user gets their own rate-limit partition, so this can't starve other tests.
        var keyed = factory.CreateClient();
        keyed.DefaultRequestHeaders.Add("X-Api-Key", key!.Token);

        // Hit the stricter "sensitive" policy (10/min) — deterministic and cheap. The URL is
        // SSRF-blocked so each preview returns fast without real network I/O.
        HttpResponseMessage? throttled = null;
        for (var i = 0; i < 15; i++)
        {
            var response = await keyed.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/x" });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }
        }

        Assert.NotNull(throttled);
        Assert.True(throttled!.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Refreshing_a_token_does_not_hand_out_a_fresh_allowance()
    {
        var username = NewUsername();
        var client = factory.CreateClient();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { username, email = $"{username}@example.com", password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        login.EnsureSuccessStatusCode();
        var first = (await login.Content.ReadFromJsonAsync<TokenDto>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        for (var i = 0; i < 15; i++)
        {
            await client.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/x" });
        }

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = first.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var second = (await refresh.Content.ReadFromJsonAsync<TokenDto>())!;
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        var afterRefresh = await client.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/x" });

        // Partitioning on the token itself meant a refresh minted a brand-new bucket, so anyone
        // could reset their own limit just by refreshing. The bucket belongs to the person.
        Assert.Equal(HttpStatusCode.TooManyRequests, afterRefresh.StatusCode);
    }

    [Fact]
    public async Task One_persons_burst_does_not_throttle_another()
    {
        var heavy = factory.CreateClient();
        var heavyToken = await heavy.RegisterAndLoginAsync(NewUsername());
        heavy.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", heavyToken);

        var quiet = factory.CreateClient();
        var quietToken = await quiet.RegisterAndLoginAsync(NewUsername());
        quiet.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", quietToken);

        for (var i = 0; i < 15; i++)
        {
            await heavy.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/x" });
        }

        // Everything reaches the API from the BFF's single address, so partitioning by IP would
        // have lumped these two together.
        var response = await quiet.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/x" });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
