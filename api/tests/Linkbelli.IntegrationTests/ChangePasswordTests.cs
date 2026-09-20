using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Changing your own password while signed in.
/// </summary>
/// <remarks>
/// There was no way to do it at all: the only route through the product was the signed-out reset
/// flow, and the web app redirects a signed-in visitor away from that. So somebody who wanted to
/// rotate a password they had typed on a shared machine had to sign out and pretend to have
/// forgotten it.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ChangePasswordTests(PostgresApiFactory factory)
{
    private const string NewPassword = "N3wPassw0rd!";

    private static async Task<(HttpClient Client, string Username)> SignedInAsync(
        PostgresApiFactory factory)
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));
        return (client, username);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password });

    [Fact]
    public async Task Changes_the_password_and_the_new_one_signs_in()
    {
        var (client, username) = await SignedInAsync(factory);

        var res = await client.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        var fresh = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, username, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(fresh, username, Password)).StatusCode);
    }

    [Fact]
    public async Task Refuses_a_wrong_current_password_and_says_which_field_is_wrong()
    {
        var (client, username) = await SignedInAsync(factory);

        var res = await client.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = "NotTheOne!1", newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.True(problem!.Errors.ContainsKey("currentPassword"));

        // And the old one still works, so nothing was changed on the way to refusing.
        var fresh = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(fresh, username, Password)).StatusCode);
    }

    [Fact]
    public async Task Refuses_a_new_password_the_rules_do_not_allow()
    {
        var (client, _) = await SignedInAsync(factory);

        var res = await client.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = Password, newPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.True(problem!.Errors.ContainsKey("newPassword"));
    }

    [Fact]
    public async Task An_api_key_cannot_change_the_password_that_would_revoke_it()
    {
        var (client, _) = await SignedInAsync(factory);

        var created = await client.PostAsJsonAsync("/api/v1/me/apikeys",
            new { name = "keyring", scopes = new[] { "playlists:read", "playlists:write" } });
        created.EnsureSuccessStatusCode();
        var key = await created.Content.ReadFromJsonAsync<ApiKeyCreatedDto>();

        var byKey = factory.CreateClient();
        byKey.DefaultRequestHeaders.Add("X-Api-Key", key!.Token);

        var res = await byKey.PostAsJsonAsync("/api/v1/me/password",
            new { currentPassword = Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);
}
