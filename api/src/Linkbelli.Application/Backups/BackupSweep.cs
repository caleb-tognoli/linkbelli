using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Backups;

/// <summary>Takes the snapshots nobody remembered to ask for.</summary>
public interface IBackupSweep
{
    /// <summary>Snapshots the users who are due one. Returns how many were written.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BackupSweep(
    IAppDbContext db,
    IBackupService backups,
    ILogger<BackupSweep> logger) : IBackupSweep
{
    /// <summary>
    /// How many users one run will consider. The sweep runs hourly and each user is due weekly,
    /// so a batch this size keeps pace with a few thousand accounts while never turning one hour
    /// into a full export of the whole instance.
    /// </summary>
    public const int BatchSize = 50;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Backup.IntervalDays);

        var due = await db.Users
            .Where(u => u.BackupsEnabled && (u.LastBackupAt == null || u.LastBackupAt < cutoff))
            // Never-backed-up first, then longest-unattended, so nobody is starved by a batch
            // that is always full. Ordered on the null-ness rather than the column itself:
            // Postgres sorts NULLs last, which would put the accounts with no snapshot at all —
            // exactly the ones this exists for — at the back of the queue.
            .OrderBy(u => u.LastBackupAt != null)
            .ThenBy(u => u.LastBackupAt)
            .Take(BatchSize)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var written = 0;

        foreach (var userId in due)
        {
            try
            {
                if (await backups.CreateAsync(userId, automatic: true, cancellationToken) is not null)
                {
                    written++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One account's export failing is not a reason to leave everyone else unattended.
                logger.LogError(ex, "Could not back up {Owner}.", userId);
            }

            // Stamped whatever happened, including the failure: retrying a broken account every
            // hour would crowd out the accounts that would succeed.
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastBackupAt, DateTimeOffset.UtcNow), cancellationToken);
        }

        if (written > 0)
        {
            logger.LogInformation("Wrote {Written} backups across {Considered} accounts.", written, due.Count);
        }

        return written;
    }
}
