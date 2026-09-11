using Hangfire;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Registers the nightly cleanups: expired trash, and source run history past its retention.
/// Both were previously kept forever.
/// </summary>
public sealed class MaintenanceScheduler(
    IRecurringJobManager recurringJobs,
    ILogger<MaintenanceScheduler> logger) : IHostedService
{
    public const string JobId = "trash:purge";
    public const string RunPruneJobId = "sources:prune-runs";

    /// <summary>Nightly, off the hour so it doesn't pile onto every hourly source schedule.</summary>
    public const string Cron = "17 3 * * *";

    /// <summary>Staggered off the trash purge so the two don't contend for the same connection.</summary>
    public const string RunPruneCron = "42 3 * * *";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            recurringJobs.AddOrUpdate<ITrashService>(
                JobId, svc => svc.PurgeExpiredAsync(CancellationToken.None), Cron);

            recurringJobs.AddOrUpdate<ISourceRunRetention>(
                RunPruneJobId, svc => svc.PruneAsync(CancellationToken.None), RunPruneCron);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to schedule the nightly cleanup jobs.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
