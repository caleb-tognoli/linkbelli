using Hangfire;
using Linkbelli.Application.Archiving;
using Linkbelli.Application.Automation;
using Linkbelli.Application.Backups;
using Linkbelli.Application.Email;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Registers the recurring maintenance: expired trash, source run history past its retention,
/// re-checking link metadata that has gone stale or failed, and classifying links saved before
/// anything was asking what they were.
/// </summary>
public sealed class MaintenanceScheduler(
    IRecurringJobManager recurringJobs,
    ILogger<MaintenanceScheduler> logger) : IHostedService
{
    public const string JobId = "trash:purge";
    public const string RunPruneJobId = "sources:prune-runs";
    public const string RecheckJobId = "links:recheck";
    public const string ClassifyJobId = "links:classify";
    public const string AutomationJobId = "automation:apply";
    public const string ArchiveJobId = "links:archive";
    public const string IdempotencyJobId = "idempotency:purge";
    public const string BackupJobId = "backups:sweep";
    public const string DigestJobId = "digest:weekly";

    public const string AccountPurgeJobId = "accounts:purge";

    /// <summary>Nightly, off the hour so it doesn't pile onto every hourly source schedule.</summary>
    public const string Cron = "17 3 * * *";

    /// <summary>Staggered off the trash purge so the two don't contend for the same connection.</summary>
    public const string RunPruneCron = "42 3 * * *";

    /// <summary>Hourly, in small batches — re-checking is spread out rather than done in one burst.</summary>
    public const string RecheckCron = "23 * * * *";

    /// <summary>
    /// Every twenty minutes, in batches: this is catching up on links saved before anything
    /// asked what they were, so it wants to converge quickly and then do nothing.
    /// </summary>
    public const string ClassifyCron = "*/20 * * * *";

    /// <summary>
    /// Every minute. Items arrive by several routes and are enriched well after they land, so
    /// this is what makes the rules reliable rather than only prompt.
    /// </summary>
    public const string AutomationCron = "* * * * *";

    /// <summary>
    /// Every five minutes, ten links at a time. Each one is an outbound request to a service
    /// doing us a favour, so this is deliberately unhurried rather than a burst.
    /// </summary>
    public const string ArchiveCron = "*/5 * * * *";

    /// <summary>Hourly: keys are only remembered for a day, so this never has much to do.</summary>
    public const string IdempotencyCron = "8 * * * *";

    /// <summary>
    /// Hourly, fifty accounts at a time. Each account is only due weekly, so this is a slow
    /// rotation rather than a nightly stampede through everyone's library at once.
    /// </summary>
    public const string BackupCron = "36 * * * *";

    /// <summary>
    /// Hourly, fifty accounts at a time — and each account only weekly, so this rotates rather
    /// than mailing a whole instance in one minute. Which is also what a free provider's daily
    /// send cap requires. At nine minutes past, clear of the other hourly jobs.
    /// </summary>
    public const string DigestCron = "9 * * * *";

    /// <summary>
    /// Daily, in the small hours. The grace period is thirty days, so a few hours either way
    /// changes nothing — and this is the one sweep that destroys data rather than tidying it,
    /// which is a reason to run it when nobody is mid-sentence rather than every hour.
    /// </summary>
    public const string AccountPurgeCron = "23 3 * * *";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            recurringJobs.AddOrUpdate<ITrashService>(
                JobId, svc => svc.PurgeExpiredAsync(CancellationToken.None), Cron);

            recurringJobs.AddOrUpdate<ISourceRunRetention>(
                RunPruneJobId, svc => svc.PruneAsync(CancellationToken.None), RunPruneCron);

            recurringJobs.AddOrUpdate<ILinkRecheckService>(
                RecheckJobId, svc => svc.SweepAsync(CancellationToken.None), RecheckCron);

            recurringJobs.AddOrUpdate<ILinkClassificationSweep>(
                ClassifyJobId, svc => svc.SweepAsync(CancellationToken.None), ClassifyCron);

            recurringJobs.AddOrUpdate<IAutomationRunner>(
                AutomationJobId, svc => svc.SweepAsync(CancellationToken.None), AutomationCron);

            recurringJobs.AddOrUpdate<ILinkArchiveSweep>(
                ArchiveJobId, svc => svc.SweepAsync(CancellationToken.None), ArchiveCron);

            recurringJobs.AddOrUpdate<IIdempotencyRetention>(
                IdempotencyJobId, svc => svc.PurgeAsync(CancellationToken.None), IdempotencyCron);

            recurringJobs.AddOrUpdate<IBackupSweep>(
                BackupJobId, svc => svc.SweepAsync(CancellationToken.None), BackupCron);

            recurringJobs.AddOrUpdate<IDigestSweep>(
                DigestJobId, svc => svc.SweepAsync(CancellationToken.None), DigestCron);

            recurringJobs.AddOrUpdate<IAccountDeletionService>(
                AccountPurgeJobId, svc => svc.PurgeExpiredAsync(CancellationToken.None), AccountPurgeCron);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to schedule the nightly cleanup jobs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
