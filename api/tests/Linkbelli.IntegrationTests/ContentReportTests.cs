using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Moderation was a host blocklist and nothing else: a visitor who found something wrong had no
/// way to say so, and whoever runs the instance had no way to hear it. These cover the report,
/// the queue, and the one remedy short of deleting somebody's work.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ContentReportTests(PostgresApiFactory factory)
{
    private record ReportDto(
        Guid Id, Guid PlaylistId, string PlaylistName, string PlaylistSlug, string OwnerUsername,
        string Visibility, string ReportedBy, string Reason, string? Note, string Status,
        string? Resolution, DateTimeOffset ReportedAt, DateTimeOffset? ResolvedAt);

    private record PageDto(List<ReportDto> Items, string? NextCursor);

    private record TokenDto(string AccessToken);

    private record PlaylistViewDto(Guid Id, string Name, string Visibility);

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

    private static async Task<(Guid Id, string Slug)> NewPublicPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Public" });
        res.EnsureSuccessStatusCode();
        var created = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        return (created.Id, created.Slug);
    }

    private static async Task<HttpResponseMessage> ReportAsync(
        HttpClient client, string username, string slug, string reason = "Spam", string? note = null) =>
        await client.PostAsJsonAsync(
            $"/api/v1/public/playlists/{username}/{slug}/report", new { reason, note });

    [Fact]
    public async Task Anyone_signed_in_can_report_a_public_playlist()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (_, slug) = await NewPublicPlaylistAsync(owner, $"Reported {Guid.NewGuid():N}");

        var (reporter, reporterName) = await NewUserAsync();
        var res = await ReportAsync(reporter, ownerName, slug, "Spam", "Nothing but affiliate links.");
        res.EnsureSuccessStatusCode();

        var report = (await res.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal("Open", report.Status);
        Assert.Equal("Spam", report.Reason);
        Assert.Equal(reporterName, report.ReportedBy, ignoreCase: true);
        Assert.Equal("Nothing but affiliate links.", report.Note);
    }

    [Fact]
    public async Task An_anonymous_visitor_cannot_fill_the_queue()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (_, slug) = await NewPublicPlaylistAsync(owner, $"Anon {Guid.NewGuid():N}");

        var res = await ReportAsync(factory.CreateClient(), ownerName, slug);

        // A queue anyone can fill anonymously is a queue nobody reads.
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Reporting_your_own_playlist_is_refused()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (_, slug) = await NewPublicPlaylistAsync(owner, $"Mine {Guid.NewGuid():N}");

        var res = await ReportAsync(owner, ownerName, slug);

        // You can change your own playlist directly; the queue is for everyone else.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Reporting_twice_does_not_add_a_second_row()
    {
        var (owner, ownerName) = await NewUserAsync();
        var (_, slug) = await NewPublicPlaylistAsync(owner, $"Twice {Guid.NewGuid():N}");
        var (reporter, _) = await NewUserAsync();

        var first = await ReportAsync(reporter, ownerName, slug);
        var second = await ReportAsync(reporter, ownerName, slug);
        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var a = (await first.Content.ReadFromJsonAsync<ReportDto>())!;
        var b = (await second.Content.ReadFromJsonAsync<ReportDto>())!;

        Assert.Equal(a.Id, b.Id);
    }

    [Fact]
    public async Task A_private_playlist_cannot_be_reported()
    {
        var (owner, ownerName) = await NewUserAsync();
        var res = await owner.PostAsJsonAsync("/api/v1/playlists",
            new { name = $"Private {Guid.NewGuid():N}", visibility = "Private" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;

        var (reporter, _) = await NewUserAsync();
        var attempt = await ReportAsync(reporter, ownerName, playlist.Slug);

        // Nothing to moderate: nobody was shown it.
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
    }

    [Fact]
    public async Task The_queue_puts_open_reports_first()
    {
        var admin = await NewAdminAsync();
        var (owner, ownerName) = await NewUserAsync();
        var (_, oldSlug) = await NewPublicPlaylistAsync(owner, $"Handled {Guid.NewGuid():N}");
        var (_, newSlug) = await NewPublicPlaylistAsync(owner, $"Waiting {Guid.NewGuid():N}");

        var (reporter, _) = await NewUserAsync();
        var handled = (await (await ReportAsync(reporter, ownerName, oldSlug)).Content
            .ReadFromJsonAsync<ReportDto>())!;
        await ReportAsync(reporter, ownerName, newSlug);

        (await admin.PostAsJsonAsync($"/api/v1/admin/reports/{handled.Id}/resolve", new { dismiss = true }))
            .EnsureSuccessStatusCode();

        var page = await admin.GetFromJsonAsync<PageDto>("/api/v1/admin/reports");

        // A queue sorted purely by date buries what still needs doing.
        Assert.Equal("Open", page!.Items[0].Status);
    }

    [Fact]
    public async Task Dismissing_closes_it_and_leaves_the_playlist_alone()
    {
        var admin = await NewAdminAsync();
        var (owner, ownerName) = await NewUserAsync();
        var (playlistId, slug) = await NewPublicPlaylistAsync(owner, $"Fine {Guid.NewGuid():N}");
        var (reporter, _) = await NewUserAsync();
        var report = (await (await ReportAsync(reporter, ownerName, slug)).Content
            .ReadFromJsonAsync<ReportDto>())!;

        var resolved = await admin.PostAsJsonAsync($"/api/v1/admin/reports/{report.Id}/resolve",
            new { dismiss = true, resolution = "Nothing wrong with it." });
        resolved.EnsureSuccessStatusCode();

        var after = (await resolved.Content.ReadFromJsonAsync<ReportDto>())!;
        Assert.Equal("Dismissed", after.Status);
        Assert.Equal("Nothing wrong with it.", after.Resolution);

        var playlist = await owner.GetFromJsonAsync<PlaylistViewDto>($"/api/v1/playlists/{playlistId}");
        Assert.Equal("Public", playlist!.Visibility);
    }

    [Fact]
    public async Task A_takedown_makes_it_private_rather_than_destroying_it()
    {
        var admin = await NewAdminAsync();
        var (owner, ownerName) = await NewUserAsync();
        var (playlistId, slug) = await NewPublicPlaylistAsync(owner, $"Bad {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlistId, 2);
        var (reporter, _) = await NewUserAsync();
        var report = (await (await ReportAsync(reporter, ownerName, slug, "Illegal")).Content
            .ReadFromJsonAsync<ReportDto>())!;

        (await admin.PostAsJsonAsync($"/api/v1/admin/reports/{report.Id}/resolve",
            new { takeDown = true, resolution = "Taken down." })).EnsureSuccessStatusCode();

        // It stops being published; its owner keeps their work. Deleting somebody's collection
        // over a report is not recoverable.
        var playlist = await owner.GetFromJsonAsync<PlaylistViewDto>($"/api/v1/playlists/{playlistId}");
        Assert.Equal("Private", playlist!.Visibility);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await factory.CreateClient().GetAsync($"/api/v1/public/playlists/{ownerName}/{slug}")).StatusCode);
    }

    [Fact]
    public async Task A_takedown_is_written_into_the_audit_trail()
    {
        var admin = await NewAdminAsync();
        var (owner, ownerName) = await NewUserAsync();
        var (playlistId, slug) = await NewPublicPlaylistAsync(owner, $"Audited {Guid.NewGuid():N}");
        var (reporter, _) = await NewUserAsync();
        var report = (await (await ReportAsync(reporter, ownerName, slug)).Content
            .ReadFromJsonAsync<ReportDto>())!;

        (await admin.PostAsJsonAsync($"/api/v1/admin/reports/{report.Id}/resolve", new { takeDown = true }))
            .EnsureSuccessStatusCode();

        var audit = await admin.GetFromJsonAsync<AuditPage>($"/api/v1/admin/audit?targetId={playlistId}");

        Assert.Contains(audit!.Items, e => e.Action == "admin.playlist.takedown");
    }

    private record AuditRow(string Action, bool AsAdmin);

    private record AuditPage(List<AuditRow> Items);

    [Fact]
    public async Task Only_an_admin_can_read_or_close_the_queue()
    {
        var (user, _) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/reports")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync($"/api/v1/admin/reports/{Guid.NewGuid()}/resolve", new { dismiss = true }))
                .StatusCode);
    }
}
