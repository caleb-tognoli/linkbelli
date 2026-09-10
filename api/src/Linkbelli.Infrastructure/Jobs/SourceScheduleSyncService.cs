using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// On startup, reconciles Hangfire recurring jobs with the database: schedules every active
/// source and unschedules every paused one. Recurring jobs persist in Hangfire storage, so this
/// mainly recovers schedule and status changes made while the app was down.
/// </summary>
public sealed class SourceScheduleSyncService(
    IServiceScopeFactory scopeFactory,
    ISourceScheduler scheduler,
    ILogger<SourceScheduleSyncService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var sources = await db.Sources
                .Select(s => new { s.Id, s.Schedule, s.Status })
                .ToListAsync(cancellationToken);

            var paused = 0;
            foreach (var source in sources)
            {
                if (source.Status == SourceStatus.Paused)
                {
                    // A source paused while the app was down still has its recurring job in
                    // Hangfire storage; drop it rather than reviving it here.
                    scheduler.Unschedule(source.Id);
                    paused++;
                }
                else
                {
                    scheduler.Schedule(source.Id, source.Schedule);
                }
            }

            if (sources.Count > 0)
            {
                logger.LogInformation(
                    "Reconciled {Count} source schedules ({Paused} paused).", sources.Count, paused);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to reconcile source schedules at startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
