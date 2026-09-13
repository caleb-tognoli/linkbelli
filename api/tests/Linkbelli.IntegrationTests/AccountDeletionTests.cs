using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Leaving.
/// </summary>
/// <remarks>
/// Export in four formats, notification preferences, API keys, backups, a bookmarklet — and no
/// way out. Data portability was taken seriously and its counterpart was absent, so somebody who
/// wanted to go had no route: their account, their public profile and their sitemap entries
/// stayed up forever, and the operator could not remove them either.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class AccountDeletionTests(PostgresApiFactory factory)
{
    private record Scheduled(DateTimeOffset DeletesAt);

    private record PublicSummary(string OwnerUsername, string Slug);

    private record Paged(List<PublicSummary> Items);

    private record Detail(Guid Id, string Visibility);

    private async Task<(HttpClient Client, string Username, Guid Id)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var id = await db.Users.Where(u => u.UserName == username).Select(u => u.Id).FirstAsync();

        return (client, username, id);
    }

    private static async Task<Guid> NewPublicPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Public" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static Task<HttpResponseMessage> DeleteMeAsync(HttpClient client, string password) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/me")
        {
            Content = JsonContent.Create(new { password }),
        });

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client, string username) =>
        await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });

    [Fact]
    public async Task Asking_to_leave_schedules_it_rather_than_doing_it()
    {
        var (client, _, _) = await NewUserAsync();

        var res = await DeleteMeAsync(client, Password);
        res.EnsureSuccessStatusCode();

        var scheduled = (await res.Content.ReadFromJsonAsync<Scheduled>())!;
        Assert.True(scheduled.DeletesAt > DateTimeOffset.UtcNow.AddDays(AccountDeletionService.GraceDays - 1));
    }

    /// <summary>
    /// A session left open on a shared machine should not be enough to end somebody's account.
    /// </summary>
    [Fact]
    public async Task The_wrong_password_does_not_do_it()
    {
        var (client, _, _) = await NewUserAsync();

        var res = await DeleteMeAsync(client, "not-the-password");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// The thing somebody leaving actually wants: stop showing my stuff. Immediately, not in
    /// thirty days.
    /// </summary>
    [Fact]
    public async Task Their_public_playlists_come_down_at_once()
    {
        var (client, username, _) = await NewUserAsync();
        var playlist = await NewPublicPlaylistAsync(client, $"Published {Guid.NewGuid():N}");

        var before = await factory.CreateClient()
            .GetFromJsonAsync<Paged>($"/api/v1/public/users/{username}/playlists");
        Assert.NotEmpty(before!.Items);

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();

        var after = await factory.CreateClient()
            .GetFromJsonAsync<Paged>($"/api/v1/public/users/{username}/playlists");
        Assert.Empty(after!.Items);

        // And the playlist itself is private, which is what every public read already filters on.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var stored = await db.Playlists.FirstAsync(p => p.Id == playlist);
        Assert.Equal(Core.Entities.PlaylistVisibility.Private, stored.Visibility);
    }

    /// <summary>
    /// Coming back is changing your mind. Making somebody find a separate "actually, no" button
    /// after signing in successfully would be a worse version of the same answer.
    /// </summary>
    [Fact]
    public async Task Signing_in_again_calls_it_off_and_puts_everything_back()
    {
        var (client, username, _) = await NewUserAsync();
        var playlist = await NewPublicPlaylistAsync(client, $"Back again {Guid.NewGuid():N}");

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();
        (await SignInAsync(factory.CreateClient(), username)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var user = await db.Users.FirstAsync(u => u.UserName == username);
        Assert.Null(user.DeletionRequestedAt);

        // Exactly what was published before, rather than everything or nothing.
        var stored = await db.Playlists.FirstAsync(p => p.Id == playlist);
        Assert.Equal(Core.Entities.PlaylistVisibility.Public, stored.Visibility);
        Assert.Null(stored.VisibilityBeforeHiding);
    }

    /// <summary>A playlist that was already private stays private when the account comes back.</summary>
    [Fact]
    public async Task A_private_playlist_is_not_published_by_coming_back()
    {
        var (client, username, _) = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"Mine {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        var playlist = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();
        (await SignInAsync(factory.CreateClient(), username)).EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<Detail>($"/api/v1/playlists/{playlist}");
        Assert.Equal("Private", reread!.Visibility);
    }

    /// <summary>
    /// An account on its way out should not still be fetching pages on a schedule.
    /// </summary>
    [Fact]
    public async Task Their_sources_stop()
    {
        var (client, _, userId) = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Feed {Guid.NewGuid():N}",
            type = "Rss",
            config = new Dictionary<string, string> { ["feedUrl"] = $"https://f{Guid.NewGuid():N}.example/rss" },
            schedule = "0 6 * * *",
            playlistIds = Array.Empty<Guid>(),
        });
        res.EnsureSuccessStatusCode();

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var statuses = await db.Sources.Where(s => s.OwnerId == userId).Select(s => s.Status).ToListAsync();

        Assert.All(statuses, st => Assert.NotEqual(Core.Entities.SourceStatus.Active, st));
    }

    /// <summary>Pressing it twice means the same as pressing it once.</summary>
    [Fact]
    public async Task Asking_twice_is_not_an_error()
    {
        var (client, _, _) = await NewUserAsync();

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();
        var again = await DeleteMeAsync(client, Password);

        again.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The purge itself. Everything the account owned goes; the global link rows and the audit
    /// trail do not.
    /// </summary>
    [Fact]
    public async Task After_the_grace_period_everything_of_theirs_goes_and_the_shared_rows_stay()
    {
        var (client, username, userId) = await NewUserAsync();
        var playlist = await NewPublicPlaylistAsync(client, $"Doomed {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        (await DeleteMeAsync(client, Password)).EnsureSuccessStatusCode();

        // Wind the clock back past the grace period.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u.SetProperty(
                    x => x.DeletionRequestedAt,
                    DateTimeOffset.UtcNow.AddDays(-AccountDeletionService.GraceDays - 1)));
        }

        using (var scope = factory.Services.CreateScope())
        {
            var deletion = scope.ServiceProvider.GetRequiredService<IAccountDeletionService>();
            Assert.True(await deletion.PurgeExpiredAsync() > 0);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

            Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
            Assert.False(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => p.Id == playlist));
            Assert.False(await db.PlaylistItems.IgnoreQueryFilters().AnyAsync(i => seeded.Contains(i.Id)));

            // The links themselves are global and deduplicated — the row for a page this account
            // saved is the same row anybody else saved it under. Deleting one would reach into
            // other people's libraries.
            Assert.True(await db.Links.AnyAsync());

            // The audit trail is the record of what happened on the instance. A record its
            // subject can erase is not one.
            Assert.True(await db.AuditEntries.AnyAsync(e => e.TargetId == userId));
        }

        // And the name is free again.
        var reused = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register", new { username, email = $"{username}@example.com", password = Password });
        reused.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A fork is an independent copy with its own item rows. Somebody who kept a copy of your
    /// list keeps it — that was the point of taking one.
    /// </summary>
    [Fact]
    public async Task A_fork_somebody_took_survives_the_original_owner_leaving()
    {
        var (author, authorName, authorId) = await NewUserAsync();
        var (reader, _, _) = await NewUserAsync();

        var res = await author.PostAsJsonAsync(
            "/api/v1/playlists", new { name = $"Forked {Guid.NewGuid():N}", visibility = "Public" });
        res.EnsureSuccessStatusCode();
        var original = (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
        await factory.SeedEnrichedItemsAsync(original.Id, 2);

        var forked = await reader.PostAsync(
            $"/api/v1/public/playlists/{authorName}/{original.Slug}/fork", null);
        forked.EnsureSuccessStatusCode();
        var fork = (await forked.Content.ReadFromJsonAsync<PlaylistDto>())!;

        (await DeleteMeAsync(author, Password)).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            await db.Users
                .Where(u => u.Id == authorId)
                .ExecuteUpdateAsync(u => u.SetProperty(
                    x => x.DeletionRequestedAt,
                    DateTimeOffset.UtcNow.AddDays(-AccountDeletionService.GraceDays - 1)));
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAccountDeletionService>().PurgeExpiredAsync();
        }

        var mine = await reader.GetFromJsonAsync<PlaylistDto>($"/api/v1/playlists/{fork.Id}");
        Assert.Equal(2, mine!.ItemCount);
    }
}
