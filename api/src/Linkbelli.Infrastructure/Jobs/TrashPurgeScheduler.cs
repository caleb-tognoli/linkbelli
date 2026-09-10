using Hangfire;
using Linkbelli.Application.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Registers the nightly purge of expired trash. Soft-deleted rows were previously kept forever;
/// this is what actually ends their life once they are past the restore window.
/// </summary>
public sealed class TrashPurgeScheduler(
    IRecurringJobManager recurringJobs,
    ILogger<TrashPurgeScheduler> logger) : IHostedService
{
    public const string JobId = "trash:purge";

    /// <summary>Nightly, off the hour so it doesn't pile onto every hourly source schedule.</summary>
    public const string Cron = "17 3 * * *";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            recurringJobs.AddOrUpdate<ITrashService>(
                JobId, svc => svc.PurgeExpiredAsync(CancellationToken.None), Cron);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to schedule the trash purge job.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
