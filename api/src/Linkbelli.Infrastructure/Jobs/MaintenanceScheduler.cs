using Hangfire;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Registers the recurring maintenance: expired trash, source run history past its retention,
/// and re-checking link metadata that has gone stale or failed.
/// </summary>
public sealed class MaintenanceScheduler(
    IRecurringJobManager recurringJobs,
    ILogger<MaintenanceScheduler> logger) : IHostedService
{
    public const string JobId = "trash:purge";
    public const string RunPruneJobId = "sources:prune-runs";
    public const string RecheckJobId = "links:recheck";

    /// <summary>Nightly, off the hour so it doesn't pile onto every hourly source schedule.</summary>
    public const string Cron = "17 3 * * *";

    /// <summary>Staggered off the trash purge so the two don't contend for the same connection.</summary>
    public const string RunPruneCron = "42 3 * * *";

    /// <summary>Hourly, in small batches — re-checking is spread out rather than done in one burst.</summary>
    public const string RecheckCron = "23 * * * *";

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
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to schedule the nightly cleanup jobs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
