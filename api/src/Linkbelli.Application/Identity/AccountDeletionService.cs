using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Services;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Identity;

/// <summary>
/// Leaving.
/// </summary>
/// <remarks>
/// Export in four formats, notification preferences, API keys, backups, a bookmarklet — and no
/// way out. Data portability was taken seriously and its counterpart was missing entirely, so
/// somebody who wanted to go had no route: their account, their public profile and their sitemap
/// entries stayed up forever, and the operator could not remove them either.
///
/// The hard questions, answered deliberately rather than by whatever the cascade happened to do:
///
/// - **Shared links stay.** <see cref="Link"/> rows are global and deduplicated by URL: the row
///   for a page you saved is the same row somebody else saved. Deleting it would reach into other
///   people's libraries, so only the join rows go.
/// - **Forks stay.** A fork is an independent copy with its own item rows. Somebody who kept a
///   copy of your list keeps it; that was the point of taking one.
/// - **Followers lose the playlist**, because the playlist is gone. That is what deletion means.
/// - **Audit entries stay.** They are the record of what happened on the instance, including
///   what administrators did, and a record that can be erased by its subject is not one.
/// - **The username is held through the grace period and released on purge.** Freeing it sooner
///   would let somebody take a name that is still attached to a live profile.
/// </remarks>
public interface IAccountDeletionService
{
    /// <summary>
    /// Asks for the account to go. Confirmed by password: this is not an action to take by
    /// accident, and a session left open on a shared machine should not be enough.
    /// </summary>
    Task<DateTimeOffset> RequestAsync(Guid userId, string password, CancellationToken ct = default);

    /// <summary>Changes their mind. Puts back exactly what was published before.</summary>
    Task CancelAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Removes the accounts whose grace period has run out. Returns how many went.</summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AccountDeletionService(
    IAppDbContext db,
    UserManager<ApplicationUser> users,
    IAuditLog audit,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    /// <summary>
    /// How long an account waits before it actually goes.
    /// </summary>
    /// <remarks>
    /// The same thirty days the trash keeps a deleted playlist, for the same reason: this is the
    /// one button in the product that cannot be undone by an apology, and the people who press it
    /// in anger are the people the window is for.
    /// </remarks>
    public const int GraceDays = 30;

    /// <summary>Accounts purged per sweep, so one busy night cannot run for an hour.</summary>
    private const int BatchSize = 20;

    public async Task<DateTimeOffset> RequestAsync(
        Guid userId, string password, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Account not found.");

        if (user.DeletionRequestedAt is { } already)
        {
            // Not an error: pressing it twice means the same thing as pressing it once, and
            // telling somebody their deletion failed would be alarming and false.
            return already.AddDays(GraceDays);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            throw new ValidationException("password", "That password is not right.");
        }

        var now = DateTimeOffset.UtcNow;
        user.DeletionRequestedAt = now;
        await users.UpdateAsync(user);

        // Every session ends here. Somebody who has asked to leave should not still be signed in
        // on the three devices they forgot about.
        await users.UpdateSecurityStampAsync(user);

        // Stop the outbound half immediately: an account on its way out should not still be
        // fetching pages on a schedule or mailing anybody.
        await db.Sources
            .Where(s => s.OwnerId == userId && s.Status == SourceStatus.Active)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SourceStatus.Paused), ct);

        await AccountVisibility.HideAsync(db, userId, ct);

        await audit.RecordAsync(
            userId, "account.deletion.requested", "User", userId,
            $"Scheduled for {now.AddDays(GraceDays):yyyy-MM-dd}.", ct: ct);

        return now.AddDays(GraceDays);
    }

    public async Task CancelAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("Account not found.");

        if (user.DeletionRequestedAt is null)
        {
            return;
        }

        user.DeletionRequestedAt = null;
        await users.UpdateAsync(user);

        await AccountVisibility.RestoreAsync(db, userId, ct);

        // Sources are left paused on purpose. Restarting a fetcher on somebody's behalf is a
        // decision they can make in one click, and doing it for them is how an account that came
        // back to be closed properly starts making outbound requests again.
        await audit.RecordAsync(userId, "account.deletion.cancelled", "User", userId, ct: ct);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-GraceDays);

        var doomed = await db.Users
            .Where(u => u.DeletionRequestedAt != null && u.DeletionRequestedAt <= cutoff)
            .OrderBy(u => u.DeletionRequestedAt)
            .Take(BatchSize)
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var userId in doomed)
        {
            await PurgeOneAsync(userId, ct);
        }

        if (doomed.Count > 0)
        {
            logger.LogInformation("Purged {Count} accounts past their grace period.", doomed.Count);
        }

        return doomed.Count;
    }

    /// <summary>
    /// Everything one account owns, gone for good.
    /// </summary>
    /// <remarks>
    /// Hard deletes, not soft ones: the soft-delete convention exists so a person can undo a
    /// mistake, and this is the one place where leaving the rows behind would defeat the whole
    /// point of the operation.
    ///
    /// Ordered so that nothing is orphaned on the way: the join rows before the things they join.
    /// </remarks>
    private async Task PurgeOneAsync(Guid userId, CancellationToken ct)
    {
        var playlists = db.Playlists.IgnoreQueryFilters().Where(p => p.OwnerId == userId).Select(p => p.Id);

        await db.PlaylistItemTags.IgnoreQueryFilters()
            .Where(t => playlists.Contains(t.PlaylistItem!.PlaylistId)).ExecuteDeleteAsync(ct);
        await db.PlaylistTags.IgnoreQueryFilters()
            .Where(t => playlists.Contains(t.PlaylistId)).ExecuteDeleteAsync(ct);
        await db.PlaylistItems.IgnoreQueryFilters()
            .Where(i => playlists.Contains(i.PlaylistId)).ExecuteDeleteAsync(ct);
        await db.PlaylistSources.IgnoreQueryFilters()
            .Where(ps => playlists.Contains(ps.PlaylistId)).ExecuteDeleteAsync(ct);
        await db.PlaylistMembers.IgnoreQueryFilters()
            .Where(m => playlists.Contains(m.PlaylistId) || m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PlaylistLikes.IgnoreQueryFilters()
            .Where(l => playlists.Contains(l.PlaylistId) || l.UserId == userId).ExecuteDeleteAsync(ct);
        await db.PlaylistPreferences.IgnoreQueryFilters()
            .Where(pp => playlists.Contains(pp.PlaylistId) || pp.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.Follows.IgnoreQueryFilters()
            .Where(f => playlists.Contains(f.PlaylistId!.Value) || f.FollowerId == userId || f.FollowedUserId == userId)
            .ExecuteDeleteAsync(ct);
        await db.FolderPlaylists.IgnoreQueryFilters()
            .Where(fp => fp.OwnerId == userId || playlists.Contains(fp.PlaylistId)).ExecuteDeleteAsync(ct);

        // A fork is somebody else's copy with its own rows, so it keeps every link it holds. All
        // it loses is the pointer back, which no longer names anything.
        await db.Playlists.IgnoreQueryFilters()
            .Where(p => playlists.Contains(p.ForkedFromPlaylistId!.Value))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.ForkedFromPlaylistId, (Guid?)null), ct);

        await db.SourceRuns.IgnoreQueryFilters()
            .Where(r => r.Source!.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.Sources.IgnoreQueryFilters().Where(s => s.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.Playlists.IgnoreQueryFilters().Where(p => p.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.Folders.IgnoreQueryFilters().Where(f => f.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.SavedSearches.IgnoreQueryFilters().Where(ss => ss.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.Backups.IgnoreQueryFilters().Where(b => b.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.ApiKeys.IgnoreQueryFilters().Where(k => k.UserId == userId).ExecuteDeleteAsync(ct);
        await db.AutomationRules.IgnoreQueryFilters().Where(r => r.OwnerId == userId).ExecuteDeleteAsync(ct);
        await db.UserQuotas.IgnoreQueryFilters().Where(q => q.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ContentReports.IgnoreQueryFilters().Where(r => r.ReporterId == userId).ExecuteDeleteAsync(ct);

        // Links and Hosts are untouched. They are global and deduplicated — the row for a page
        // this account saved is the same row everybody else saved it under — so deleting one
        // would reach into other people's libraries.
        //
        // Audit entries are untouched too. They are the record of what happened on this
        // instance, administrators included, and a record its subject can erase is not one.

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            await users.DeleteAsync(user);
        }

        await audit.RecordAsync(userId, "account.deleted", "User", userId, ct: ct);
    }
}
