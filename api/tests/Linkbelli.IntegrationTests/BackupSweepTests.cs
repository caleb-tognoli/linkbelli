using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Backups;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The half of backups that matters: the one nobody asked for. These cover who the sweep picks
/// up, who it leaves alone, and that it does not come back to the same account every hour.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BackupSweepTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name);

    private async Task<(HttpClient Client, Guid UserId)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var id = await db.Users.Where(u => u.UserName == username).Select(u => u.Id).FirstAsync();

        return (client, id);
    }

    private static async Task NewPlaylistAsync(HttpClient client, string name) =>
        (await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Private" }))
            .EnsureSuccessStatusCode();

    /// <summary>Makes a user due, or not, by moving when the sweep last looked at them.</summary>
    private async Task SetLastBackupAsync(Guid userId, DateTimeOffset? at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastBackupAt, at));
    }

    private async Task SetEnabledAsync(Guid userId, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.BackupsEnabled, enabled));
    }

    /// <summary>
    /// Read straight from the table rather than through the endpoint: the sweep runs as nobody,
    /// and what is being checked is what it wrote, not what a session can see.
    /// </summary>
    private async Task<List<Backup>> BackupsOfAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Backups.Where(b => b.OwnerId == userId)
            .OrderByDescending(b => b.CreationTime).ToListAsync();
    }

    private async Task<DateTimeOffset?> LastBackupAtAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.LastBackupAt).FirstAsync();
    }

    private async Task SweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IBackupSweep>().SweepAsync();
    }

    /// <summary>
    /// Marks every account as just attended to, so a test can then say which ones are due.
    /// </summary>
    /// <remarks>
    /// The suite shares one database and every account in it is due a first backup, which is far
    /// more than one batch holds. In production a full batch only means the rotation takes a few
    /// hours; here it means a test's own account may simply not be reached. Quieting the rest
    /// makes the question "who does the sweep pick" answerable.
    /// </remarks>
    private async Task QuietEveryoneAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Users.ExecuteUpdateAsync(
            s => s.SetProperty(u => u.LastBackupAt, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task An_account_nobody_has_backed_up_gets_one()
    {
        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Unattended {Guid.NewGuid():N}");
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, null);

        await SweepAsync();

        var backups = await BackupsOfAsync(userId);
        Assert.Single(backups);
        // Marked as the schedule's work, so a listing can tell it apart from one a person took.
        Assert.True(backups[0].Automatic);
        Assert.NotNull(await LastBackupAtAsync(userId));
    }

    [Fact]
    public async Task An_account_backed_up_this_week_is_left_alone()
    {
        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Recent {Guid.NewGuid():N}");
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, DateTimeOffset.UtcNow.AddDays(-1));

        await SweepAsync();

        // Otherwise every hourly run would re-export every account on the instance.
        Assert.Empty(await BackupsOfAsync(userId));
    }

    [Fact]
    public async Task An_account_that_turned_them_off_is_left_alone()
    {
        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Opted out {Guid.NewGuid():N}");
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, null);
        await SetEnabledAsync(userId, false);

        await SweepAsync();

        Assert.Empty(await BackupsOfAsync(userId));
        // Not even stamped: the sweep has no business touching a row it was told to skip.
        Assert.Null(await LastBackupAtAsync(userId));
    }

    [Fact]
    public async Task A_stale_account_whose_library_has_not_changed_keeps_the_snapshot_it_has()
    {
        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Dormant {Guid.NewGuid():N}");
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, null);
        await SweepAsync();
        var first = (await BackupsOfAsync(userId)).Single();

        // A week later, with nothing having happened in between.
        await SetLastBackupAsync(userId, DateTimeOffset.UtcNow.AddDays(-Backup.IntervalDays - 1));
        await SweepAsync();

        var after = await BackupsOfAsync(userId);
        Assert.Single(after);
        Assert.Equal(first.Id, after[0].Id);
        // Still stamped, so the account is not reconsidered again in an hour.
        Assert.True(await LastBackupAtAsync(userId) > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task A_stale_account_whose_library_changed_gets_a_second_snapshot()
    {
        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Active {Guid.NewGuid():N}");
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, null);
        await SweepAsync();

        await NewPlaylistAsync(client, $"Added later {Guid.NewGuid():N}");
        await SetLastBackupAsync(userId, DateTimeOffset.UtcNow.AddDays(-Backup.IntervalDays - 1));
        await SweepAsync();

        var after = await BackupsOfAsync(userId);
        Assert.Equal(2, after.Count);
        Assert.Equal(2, after[0].PlaylistCount);
        Assert.Equal(1, after[1].PlaylistCount);
    }

    [Fact]
    public async Task An_account_with_no_snapshot_is_reached_before_one_that_has_some()
    {
        var crowd = new List<Guid>();
        for (var i = 0; i < BackupSweep.BatchSize; i++)
        {
            var (member, memberId) = await NewUserAsync();
            await NewPlaylistAsync(member, $"Crowd {i} {Guid.NewGuid():N}");
            crowd.Add(memberId);
        }

        var (client, userId) = await NewUserAsync();
        await NewPlaylistAsync(client, $"Never {Guid.NewGuid():N}");

        // Exactly a full batch of accounts already waiting, each with a snapshot behind it, and
        // one account with none. Only the crowd and the subject are due.
        await QuietEveryoneAsync();
        foreach (var memberId in crowd)
        {
            await SetLastBackupAsync(memberId, DateTimeOffset.UtcNow.AddDays(-Backup.IntervalDays - 30));
        }

        await SetLastBackupAsync(userId, null);

        await SweepAsync();

        // Postgres sorts NULLs last, so ordering on the column alone would put the account that
        // has never been backed up behind every account that already has a copy.
        Assert.Single(await BackupsOfAsync(userId));
    }

    [Fact]
    public async Task What_the_sweep_wrote_is_the_account_it_was_for()
    {
        var (client, userId) = await NewUserAsync();
        var name = $"Only mine {Guid.NewGuid():N}";
        await NewPlaylistAsync(client, name);
        await QuietEveryoneAsync();
        await SetLastBackupAsync(userId, null);

        await SweepAsync();

        var json = BackupService.Decompress((await BackupsOfAsync(userId)).Single().Content);
        Assert.Contains(name, json);
    }
}
