using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

/// <summary>
/// Drains the enrichment queue, processing each link in its own DI scope. On startup it
/// sweeps the durable backlog (links with EnrichedAt == null) back onto the queue, so
/// work isn't lost across restarts even though the queue itself is in-memory.
/// </summary>
public sealed class LinkEnrichmentWorker(
    ILinkEnrichmentQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LinkEnrichmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepBacklogAsync(stoppingToken);

        await foreach (var linkId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var enricher = scope.ServiceProvider.GetRequiredService<ILinkEnricher>();
                await enricher.EnrichAsync(linkId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error enriching link {LinkId}", linkId);
            }
        }
    }

    private async Task SweepBacklogAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var pending = await db.Links
                .Where(l => l.EnrichedAt == null)
                .Select(l => l.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in pending)
            {
                queue.Enqueue(id);
            }

            if (pending.Count > 0)
            {
                logger.LogInformation("Enqueued {Count} unenriched links from the backlog.", pending.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to sweep the enrichment backlog at startup.");
        }
    }
}
