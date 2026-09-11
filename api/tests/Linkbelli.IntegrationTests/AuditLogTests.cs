using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Admin actions reach into other people's data and some user actions destroy rows outright.
/// Neither left any trace: the only record that a host had been blocked, or a trash emptied, was
/// the absence of what used to be there.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AuditLogTests(PostgresApiFactory factory)
{
    private record EntryDto(
        Guid Id, Guid? ActorId, string ActorName, bool AsAdmin, string Action, string? TargetType,
        Guid? TargetId, string? Summary, string? Details, DateTimeOffset At);

    private record PageDto(List<EntryDto> Items, string? NextCursor);

    private record TokenDto(string AccessToken);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private async Task<HttpClient> NewAdminAsync()
    {
        var username = NewUsername();
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

            await users.AddToRoleAsync((await users.FindByNameAsync(username))!, "Admin");
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;

        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return admin;
    }

    private static async Task<PageDto> AuditAsync(HttpClient admin, string query = "") =>
        (await admin.GetFromJsonAsync<PageDto>($"/api/v1/admin/audit{query}"))!;

    [Fact]
    public async Task Blocking_a_host_is_recorded_with_who_did_it()
    {
        var admin = await NewAdminAsync();
        var hostname = $"blocked{Guid.NewGuid():N}.example";

        (await admin.PutAsJsonAsync("/api/v1/admin/hosts", new { hostname, blocked = true }))
            .EnsureSuccessStatusCode();

        var page = await AuditAsync(admin, "?action=admin.host");

        var entry = Assert.Single(page.Items, e => e.Summary != null && e.Summary.Contains(hostname));
        Assert.Equal("admin.host.block", entry.Action);
        Assert.True(entry.AsAdmin);
        Assert.NotEmpty(entry.ActorName);
        Assert.Contains(hostname, entry.Details);
    }

    [Fact]
    public async Task Unblocking_is_a_different_action_from_blocking()
    {
        var admin = await NewAdminAsync();
        var hostname = $"toggled{Guid.NewGuid():N}.example";

        (await admin.PutAsJsonAsync("/api/v1/admin/hosts", new { hostname, blocked = true }))
            .EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/v1/admin/hosts", new { hostname, blocked = false }))
            .EnsureSuccessStatusCode();

        var page = await AuditAsync(admin, "?action=admin.host");
        var mine = page.Items.Where(e => e.Summary != null && e.Summary.Contains(hostname)).ToList();

        Assert.Equal(2, mine.Count);
        Assert.Contains(mine, e => e.Action == "admin.host.block");
        Assert.Contains(mine, e => e.Action == "admin.host.unblock");
    }

    [Fact]
    public async Task A_quota_change_keeps_the_before_and_the_after()
    {
        var admin = await NewAdminAsync();
        var (_, _) = await NewUserAsync();
        var users = await admin.GetFromJsonAsync<List<AdminUserDto>>("/api/v1/admin/users?limit=1");
        var target = users!.Single();

        (await admin.PutAsJsonAsync($"/api/v1/admin/users/{target.Id}/quota",
            new { maxSources = 99, maxRunsPerDay = 50, maxItemsPerRun = 200 }))
            .EnsureSuccessStatusCode();

        var page = await AuditAsync(admin, $"?targetId={target.Id}");

        // "Who raised this person's limits, and from what" is exactly the question asked after.
        var entry = Assert.Single(page.Items, e => e.Action == "admin.quota.set");
        Assert.Contains("\"before\"", entry.Details);
        Assert.Contains("\"after\"", entry.Details);
        Assert.Contains("99", entry.Details);
    }

    private record AdminUserDto(Guid Id, string? Username);

    [Fact]
    public async Task Deleting_a_playlist_records_what_went_with_it()
    {
        var admin = await NewAdminAsync();
        var (user, username) = await NewUserAsync();

        var created = await user.PostAsJsonAsync("/api/v1/playlists", new { name = $"Doomed {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!;
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        (await user.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();

        var page = await AuditAsync(admin, $"?targetId={playlist.Id}");

        var entry = Assert.Single(page.Items, e => e.Action == "playlist.delete");
        Assert.Equal(username, entry.ActorName, ignoreCase: true);
        // Not an admin action, and the flag says so.
        Assert.False(entry.AsAdmin);
        Assert.Contains("3", entry.Summary);
    }

    [Fact]
    public async Task Emptying_the_trash_is_recorded_because_it_is_the_one_that_really_deletes()
    {
        var admin = await NewAdminAsync();
        var (user, _) = await NewUserAsync();

        var created = await user.PostAsJsonAsync("/api/v1/playlists", new { name = $"Purged {Guid.NewGuid():N}" });
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!;
        (await user.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();

        (await user.DeleteAsync("/api/v1/trash")).EnsureSuccessStatusCode();

        var page = await AuditAsync(admin, "?action=trash.empty");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, e => Assert.False(e.AsAdmin));
    }

    [Fact]
    public async Task Only_an_admin_can_read_the_trail()
    {
        var (user, _) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/audit")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/v1/admin/audit")).StatusCode);
    }

    [Fact]
    public async Task The_action_filter_matches_by_prefix()
    {
        var admin = await NewAdminAsync();
        (await admin.PutAsJsonAsync("/api/v1/admin/hosts",
            new { hostname = $"prefix{Guid.NewGuid():N}.example", blocked = true }))
            .EnsureSuccessStatusCode();

        // "admin." finds every admin action without having to enumerate them.
        var page = await AuditAsync(admin, "?action=admin.");

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, e => Assert.StartsWith("admin.", e.Action));
    }

    [Fact]
    public async Task The_trail_is_newest_first_and_pages()
    {
        var admin = await NewAdminAsync();
        foreach (var _ in Enumerable.Range(0, 3))
        {
            (await admin.PutAsJsonAsync("/api/v1/admin/hosts",
                new { hostname = $"paged{Guid.NewGuid():N}.example", blocked = true }))
                .EnsureSuccessStatusCode();
        }

        var first = await AuditAsync(admin, "?limit=2");
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.True(first.Items[0].At >= first.Items[1].At);

        var second = await AuditAsync(admin, $"?limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.Empty(second.Items.Select(e => e.Id).Intersect(first.Items.Select(e => e.Id)));
    }
}
