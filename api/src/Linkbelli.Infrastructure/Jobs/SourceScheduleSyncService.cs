using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// On startup, reconciles Hangfire recurring jobs with the database: (re)schedules every
/// enabled source. Recurring jobs persist in Hangfire storage, so this mainly recovers
/// schedule changes made while the app was down.
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
                .Where(s => s.Enabled)
                .Select(s => new { s.Id, s.Schedule })
                .ToListAsync(cancellationToken);

            foreach (var source in sources)
            {
                scheduler.Schedule(source.Id, source.Schedule);
            }

            if (sources.Count > 0)
            {
                logger.LogInformation("Reconciled {Count} source schedules.", sources.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to reconcile source schedules at startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
