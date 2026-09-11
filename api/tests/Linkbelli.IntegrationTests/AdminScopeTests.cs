using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Scopes existed, but there was no admin scope and the admin endpoints refused API keys outright
/// — so instance maintenance could only be run by a person with a session open. These cover what
/// a key can now reach, and the two things a scope still cannot do.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AdminScopeTests(PostgresApiFactory factory)
{
    private record TokenDto(string AccessToken);

    private record ApiKeyDto(Guid Id, string Name, string Token);

    /// <summary>Registers a user, optionally as an admin, and returns a bearer client.</summary>
    private async Task<(HttpClient Client, string Username)> NewUserAsync(bool admin = false)
    {
        var username = NewUsername();
        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync(username);

        if (admin)
        {
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync("Admin"))
            {
                await roles.CreateAsync(new IdentityRole<Guid>("Admin"));
            }

            await users.AddToRoleAsync((await users.FindByNameAsync(username))!, "Admin");
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;

        var authed = factory.CreateClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (authed, username);
    }

    /// <summary>Mints a key for the caller and returns a client that presents it.</summary>
    private async Task<HttpClient> KeyClientAsync(HttpClient owner, params string[] scopes)
    {
        var res = await owner.PostAsJsonAsync("/api/v1/me/apikeys", new { name = $"key-{Guid.NewGuid():N}", scopes });
        res.EnsureSuccessStatusCode();
        var key = (await res.Content.ReadFromJsonAsync<ApiKeyDto>())!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key.Token);
        return client;
    }

    [Fact]
    public async Task An_admin_key_with_the_read_scope_can_read_the_instance()
    {
        var (admin, _) = await NewUserAsync(admin: true);
        var key = await KeyClientAsync(admin, "admin:read");

        // The point of the feature: maintenance from a script, without a person's session token.
        (await key.GetAsync("/api/v1/admin/overview")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Reading_does_not_let_it_act()
    {
        var (admin, _) = await NewUserAsync(admin: true);
        var key = await KeyClientAsync(admin, "admin:read");

        var res = await key.PutAsJsonAsync("/api/v1/admin/hosts",
            new { hostname = $"scoped{Guid.NewGuid():N}.example", blocked = true });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task A_write_scope_can_act()
    {
        var (admin, _) = await NewUserAsync(admin: true);
        var key = await KeyClientAsync(admin, "admin:read", "admin:write");

        (await key.PutAsJsonAsync("/api/v1/admin/hosts",
            new { hostname = $"scoped{Guid.NewGuid():N}.example", blocked = true }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_scope_is_not_a_promotion()
    {
        var (ordinary, _) = await NewUserAsync(admin: false);
        var key = await KeyClientAsync(ordinary, "admin:read", "admin:write");

        // Asking for the scope is not the same as being allowed to use it. The role is checked
        // separately and cannot be granted by minting a key.
        Assert.Equal(HttpStatusCode.Forbidden, (await key.GetAsync("/api/v1/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task An_unrestricted_key_is_unrestricted_over_its_owner_not_the_instance()
    {
        var (admin, _) = await NewUserAsync(admin: true);
        var key = await KeyClientAsync(admin); // no scopes at all

        // It can do everything with its owner's own data...
        (await key.GetAsync("/api/v1/playlists")).EnsureSuccessStatusCode();

        // ...and nothing to the instance. Otherwise every general-purpose key an admin ever
        // minted would quietly be an instance-wide credential.
        Assert.Equal(HttpStatusCode.Forbidden, (await key.GetAsync("/api/v1/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task An_ordinary_scoped_key_is_untouched_by_any_of_this()
    {
        var (user, _) = await NewUserAsync();
        var key = await KeyClientAsync(user, "playlists:read");

        (await key.GetAsync("/api/v1/playlists")).EnsureSuccessStatusCode();
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await key.PostAsJsonAsync("/api/v1/playlists", new { name = "Nope" })).StatusCode);
    }

    [Fact]
    public async Task An_admin_still_reaches_everything_with_a_session()
    {
        var (admin, _) = await NewUserAsync(admin: true);

        // Interactive bearer principals were never scope-limited, and still are not.
        (await admin.GetAsync("/api/v1/admin/overview")).EnsureSuccessStatusCode();
        (await admin.GetAsync("/api/v1/admin/audit")).EnsureSuccessStatusCode();
    }
}
