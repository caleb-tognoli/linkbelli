using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Sources;

/// <inheritdoc />
public class SourceRunRetention(IAppDbContext db, ILogger<SourceRunRetention> logger) : ISourceRunRetention
{
    public async Task<int> PruneAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var succeededCutoff = now.AddDays(-ISourceRunRetention.SucceededRetentionDays);
        var failedCutoff = now.AddDays(-ISourceRunRetention.FailedRetentionDays);

        // The most recent runs per source are spared regardless of age, so a source that runs
        // monthly still has history to show. Done as one query per source rather than a single
        // grouped one: a per-group Take has no translation, and this job runs once a night over
        // as many sources as exist, which is a small number.
        var sourceIds = await db.SourceRuns.Select(r => r.SourceId).Distinct().ToListAsync(ct);

        var spareSet = new HashSet<Guid>();
        foreach (var sourceId in sourceIds)
        {
            var keep = await db.SourceRuns
                .Where(r => r.SourceId == sourceId)
                .OrderByDescending(r => r.CreationTime)
                .Take(ISourceRunRetention.AlwaysKeepPerSource)
                .Select(r => r.Id)
                .ToListAsync(ct);

            spareSet.UnionWith(keep);
        }

        var expired = await db.SourceRuns
            .Where(r => r.Status == SourceRunStatus.Failed
                ? r.CreationTime < failedCutoff
                : r.CreationTime < succeededCutoff)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var doomed = expired.Where(id => !spareSet.Contains(id)).ToList();
        if (doomed.Count == 0)
        {
            return 0;
        }

        var removed = await db.SourceRuns.Where(r => doomed.Contains(r.Id)).ExecuteDeleteAsync(ct);
        logger.LogInformation("Pruned {Count} source runs.", removed);

        return removed;
    }
}
