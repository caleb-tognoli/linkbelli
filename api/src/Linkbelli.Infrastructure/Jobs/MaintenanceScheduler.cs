using Hangfire;
using Linkbelli.Application.Automation;
using Linkbelli.Application.Enrichment;
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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to schedule the nightly cleanup jobs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
