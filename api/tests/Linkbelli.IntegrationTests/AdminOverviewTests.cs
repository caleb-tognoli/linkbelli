using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Failing sources, unreadable links and the enrichment backlog were all being recorded, and none
/// of it had anywhere to be seen. These cover the console that shows them, and who may look.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AdminOverviewTests(PostgresApiFactory factory)
{
    private record JobsDto(long Enqueued, long Processing, long Scheduled, long Failed, long Succeeded);

    private record FailingSourceDto(
        Guid Id, string Name, string OwnerUsername, int ConsecutiveFailures, string Status,
        DateTimeOffset? LastRunAt, string? LastError);

    private record HostDto(string Hostname, int LinkCount, int FailedCount);

    private record LinkErrorDto(Guid LinkId, string Url, string Status, string Error, int FailureCount);

    private record OverviewDto(
        int Users, int Playlists, int Links, int Items, int PendingEnrichment, int BrokenLinks,
        int Sources, int FailingSources, int RunsRecently, int FailedRunsRecently, int RecentDays,
        JobsDto? Jobs, List<FailingSourceDto> TopFailingSources, List<HostDto> TopHosts,
        List<LinkErrorDto> RecentErrors);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private record TokenDto(string AccessToken);

    /// <summary>Registers a user, grants the Admin role, and re-logs in so the token carries it.</summary>
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

        // Roles are baked into the bearer token at login, so re-login after the grant.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;

        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return admin;
    }

    [Fact]
    public async Task Only_an_admin_can_see_it()
    {
        var user = await NewUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/overview")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/api/v1/admin/overview")).StatusCode);
    }

    [Fact]
    public async Task The_overview_counts_what_is_actually_there()
    {
        var admin = await NewAdminAsync();
        var user = await NewUserAsync();
        var res = await user.PostAsJsonAsync("/api/v1/playlists", new { name = $"Counted {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/v1/admin/overview");

        Assert.True(overview!.Users >= 2);
        Assert.True(overview.Playlists >= 1);
        Assert.True(overview.Items >= 3);
        Assert.Equal(7, overview.RecentDays);
    }

    [Fact]
    public async Task A_failing_source_is_surfaced_with_its_last_error()
    {
        var admin = await NewAdminAsync();
        var user = await NewUserAsync();

        var created = await user.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Broken {Guid.NewGuid():N}",
            type = "Rss",
            config = new { feedUrl = $"https://broken.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 * * * *",
        });
        created.EnsureSuccessStatusCode();
        var source = (await created.Content.ReadFromJsonAsync<SourceIdDto>())!;

        await MarkFailingAsync(source.Id, "Selector matched nothing.");

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/v1/admin/overview");

        var row = Assert.Single(overview!.TopFailingSources, s => s.Id == source.Id);
        Assert.Equal(5, row.ConsecutiveFailures);
        Assert.Equal("Failing", row.Status);
        // The owner's name, because the next step is telling them.
        Assert.NotEmpty(row.OwnerUsername);
        Assert.Equal("Selector matched nothing.", row.LastError);
    }

    private record SourceIdDto(Guid Id, string Name);

    /// <summary>Puts a source in the state the console exists to show.</summary>
    private async Task MarkFailingAsync(Guid sourceId, string error)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var source = await db.Sources.FirstAsync(s => s.Id == sourceId);
        source.Status = SourceStatus.Failing;
        source.ConsecutiveFailures = 5;
        source.LastRunAt = DateTimeOffset.UtcNow;

        db.SourceRuns.Add(new SourceRun
        {
            SourceId = sourceId,
            Status = SourceRunStatus.Failed,
            Error = error,
            FinishedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_enrichment_backlog_is_reported()
    {
        var admin = await NewAdminAsync();
        var user = await NewUserAsync();
        var res = await user.PostAsJsonAsync("/api/v1/playlists", new { name = $"Backlog {Guid.NewGuid():N}" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        var before = (await admin.GetFromJsonAsync<OverviewDto>("/api/v1/admin/overview"))!.PendingEnrichment;
        await AddPendingLinkAsync();
        var after = (await admin.GetFromJsonAsync<OverviewDto>("/api/v1/admin/overview"))!.PendingEnrichment;

        // The backlog is why a filling playlist appears to creep upward on its own.
        Assert.Equal(before + 1, after);
    }

    /// <summary>Adds a link that has not been fetched, exactly as a source run leaves one.</summary>
    private async Task AddPendingLinkAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var hostname = $"pending{Guid.NewGuid():N}.example";
        var host = new Host { Hostname = hostname };
        db.Hosts.Add(host);
        db.Links.Add(new Link
        {
            CanonicalUrl = $"https://{hostname}/waiting",
            UrlHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            HostId = host.Id,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Hosts_are_ranked_by_how_much_of_the_collection_sits_on_them()
    {
        var admin = await NewAdminAsync();
        var user = await NewUserAsync();
        var res = await user.PostAsJsonAsync("/api/v1/playlists", new { name = $"Hosts {Guid.NewGuid():N}" });
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        await factory.SeedEnrichedItemsAsync(playlist.Id, 4, url: n => $"https://busy{Guid.NewGuid():N}.example/{n}");

        var overview = await admin.GetFromJsonAsync<OverviewDto>("/api/v1/admin/overview");

        // The ranking itself, rather than "my host is in there": whether any one host makes the
        // top ten depends on everything else in the instance, which is the whole point of it.
        Assert.NotEmpty(overview!.TopHosts);
        Assert.True(overview.TopHosts.Count <= 10);
        Assert.Equal(
            overview.TopHosts.OrderByDescending(h => h.LinkCount).Select(h => h.Hostname),
            overview.TopHosts.Select(h => h.Hostname));
        Assert.All(overview.TopHosts, h => Assert.True(h.FailedCount <= h.LinkCount));
    }
}
