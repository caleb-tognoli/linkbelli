using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What an administrator can do about an account.
/// </summary>
/// <remarks>
/// The moderation half of the admin surface is well built — reports, the host blocklist, NSFW
/// overrides, the audit trail. The account half was one read and one quota setting: an admin
/// could see every user and could do nothing whatever about them. And granting admin meant
/// editing configuration and restarting, so an instance with two admins could not add a third
/// without a deploy, or remove one without a deploy either.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class AdminUserLifecycleTests(PostgresApiFactory factory)
{
    private record AdminUserDto(
        Guid Id, string? Username, bool IsAdmin, DateTimeOffset? SuspendedAt, DateTimeOffset? DeletionRequestedAt);

    private record PublicSummary(string OwnerUsername, string Slug);

    private record Paged(List<PublicSummary> Items);

    private async Task<(HttpClient Client, string Username, Guid Id)> NewUserAsync(bool admin = false)
    {
        var username = NewUsername();
        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync(username);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var user = (await users.FindByNameAsync(username))!;

        if (admin)
        {
            if (!await roles.RoleExistsAsync(AdminUserService.AdminRole))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(AdminUserService.AdminRole));
            }

            await users.AddToRoleAsync(user, AdminUserService.AdminRole);
        }

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { login = username, password = Password });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken);

        return (client, username, user.Id);
    }

    private static Task<HttpResponseMessage> SuspendAsync(HttpClient admin, Guid userId, bool suspended) =>
        admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/suspended", new { suspended });

    private static Task<HttpResponseMessage> SetAdminAsync(HttpClient admin, Guid userId, bool isAdmin) =>
        admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/admin", new { admin = isAdmin });

    private static async Task<AdminUserDto> LookUpAsync(HttpClient admin, string username) =>
        (await admin.GetFromJsonAsync<List<AdminUserDto>>($"/api/v1/admin/users?q={username}"))!
            .Single(u => u.Username == username);

    [Fact]
    public async Task Suspending_blocks_sign_in()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (_, targetName, targetId) = await NewUserAsync();

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();

        var attempt = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { login = targetName, password = Password });

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    /// <summary>
    /// Checked after the password, so this cannot be used to find out which accounts an
    /// administrator has suspended.
    /// </summary>
    [Fact]
    public async Task A_suspended_account_with_the_wrong_password_says_the_usual_thing()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (_, targetName, targetId) = await NewUserAsync();

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();

        var attempt = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { login = targetName, password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, attempt.StatusCode);
    }

    [Fact]
    public async Task Suspending_takes_their_public_playlists_down_and_reinstating_puts_them_back()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (target, targetName, targetId) = await NewUserAsync();

        var created = await target.PostAsJsonAsync(
            "/api/v1/playlists", new { name = $"Published {Guid.NewGuid():N}", visibility = "Public" });
        created.EnsureSuccessStatusCode();

        async Task<int> PublishedAsync() =>
            (await factory.CreateClient()
                .GetFromJsonAsync<Paged>($"/api/v1/public/users/{targetName}/playlists"))!.Items.Count;

        Assert.Equal(1, await PublishedAsync());

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();
        Assert.Equal(0, await PublishedAsync());

        (await SuspendAsync(admin, targetId, false)).EnsureSuccessStatusCode();
        Assert.Equal(1, await PublishedAsync());
    }

    [Fact]
    public async Task Reinstating_lets_them_back_in()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (_, targetName, targetId) = await NewUserAsync();

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();
        (await SuspendAsync(admin, targetId, false)).EnsureSuccessStatusCode();

        var attempt = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { login = targetName, password = Password });

        attempt.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_admin_can_promote_and_demote_somebody_else()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (_, targetName, targetId) = await NewUserAsync();

        Assert.False((await LookUpAsync(admin, targetName)).IsAdmin);

        (await SetAdminAsync(admin, targetId, true)).EnsureSuccessStatusCode();
        Assert.True((await LookUpAsync(admin, targetName)).IsAdmin);

        (await SetAdminAsync(admin, targetId, false)).EnsureSuccessStatusCode();
        Assert.False((await LookUpAsync(admin, targetName)).IsAdmin);
    }

    /// <summary>
    /// The last-admin problem in its most common form. Locking yourself out of your own instance
    /// takes a deploy to undo, and somebody meaning to demote a colleague and clicking their own
    /// row is not a far-fetched afternoon.
    /// </summary>
    [Fact]
    public async Task An_admin_cannot_demote_themselves()
    {
        var (admin, adminName, adminId) = await NewUserAsync(admin: true);

        var res = await SetAdminAsync(admin, adminId, false);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True((await LookUpAsync(admin, adminName)).IsAdmin);
    }

    [Fact]
    public async Task Somebody_who_is_not_an_admin_cannot_do_any_of_this()
    {
        var (ordinary, _, _) = await NewUserAsync();
        var (_, _, targetId) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await SuspendAsync(ordinary, targetId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SetAdminAsync(ordinary, targetId, true)).StatusCode);
    }

    /// <summary>
    /// The state an admin needs before deciding anything: who can already do this, who has been
    /// stopped, and who is on their way out.
    /// </summary>
    [Fact]
    public async Task The_user_list_says_what_state_each_account_is_in()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (target, targetName, targetId) = await NewUserAsync();

        var plain = await LookUpAsync(admin, targetName);
        Assert.Null(plain.SuspendedAt);
        Assert.Null(plain.DeletionRequestedAt);

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();
        Assert.NotNull((await LookUpAsync(admin, targetName)).SuspendedAt);

        (await SuspendAsync(admin, targetId, false)).EnsureSuccessStatusCode();

        var leaving = await target.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me")
        {
            Content = JsonContent.Create(new { password = Password }),
        });
        leaving.EnsureSuccessStatusCode();

        Assert.NotNull((await LookUpAsync(admin, targetName)).DeletionRequestedAt);
    }

    /// <summary>Every one of these is exactly what the audit trail exists for.</summary>
    [Fact]
    public async Task All_of_it_is_written_down()
    {
        var (admin, _, _) = await NewUserAsync(admin: true);
        var (_, _, targetId) = await NewUserAsync();

        (await SuspendAsync(admin, targetId, true)).EnsureSuccessStatusCode();

        var trail = await admin.GetStringAsync($"/api/v1/admin/audit?targetId={targetId}");

        Assert.Contains("admin.user.suspended", trail);
    }
}
