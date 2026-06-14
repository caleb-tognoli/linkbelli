using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class AdminModerationTests(PostgresApiFactory factory)
{
    private record AdminUserDto(Guid Id, string? Username, string? Email, int PlaylistCount, int SourceCount);
    private record AdminHostDto(Guid Id, string Hostname, bool Blocked, int LinkCount);

    private async Task<HttpClient> NewBearerAsync(string username)
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Registers a user, grants the Admin role, and re-logs in so the token carries the role.</summary>
    private async Task<HttpClient> NewAdminAsync(string username)
    {
        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync(username);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync("Admin"))
            {
                await roles.CreateAsync(new IdentityRole<Guid>("Admin"));
            }

            var user = await users.FindByNameAsync(username);
            await users.AddToRoleAsync(user!, "Admin");
        }

        // Roles are baked into the bearer token at login, so re-login after the grant.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;
        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return admin;
    }

    [Fact]
    public async Task Admin_can_search_users_others_are_forbidden()
    {
        var targetName = NewUsername();
        await NewBearerAsync(targetName); // a user to find

        var admin = await NewAdminAsync(NewUsername());
        var results = await admin.GetFromJsonAsync<List<AdminUserDto>>($"/api/v1/admin/users?q={targetName}");
        Assert.Contains(results!, u => u.Username == targetName);

        var nonAdmin = await NewBearerAsync(NewUsername());
        var forbidden = await nonAdmin.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Blocking_a_host_refuses_links_until_unblocked()
    {
        var admin = await NewAdminAsync(NewUsername());
        var user = await NewBearerAsync(NewUsername());
        var host = $"blk{Guid.NewGuid():N}.test";
        var url = $"https://{host}/x";

        // Block the host.
        var block = await admin.PutAsJsonAsync("/api/v1/admin/hosts", new { hostname = host, blocked = true });
        block.EnsureSuccessStatusCode();
        Assert.True((await block.Content.ReadFromJsonAsync<AdminHostDto>())!.Blocked);

        // The user can no longer add links from it.
        var blocked = await user.PostAsJsonAsync("/api/v1/links", new { url });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        // It shows in the admin blocklist.
        var list = await admin.GetFromJsonAsync<List<AdminHostDto>>($"/api/v1/admin/hosts?blocked=true&q={host}");
        Assert.Contains(list!, h => h.Hostname == host && h.Blocked);

        // Unblock → adding works again.
        await admin.PutAsJsonAsync("/api/v1/admin/hosts", new { hostname = host, blocked = false });
        var ok = await user.PostAsJsonAsync("/api/v1/links", new { url });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }
}
