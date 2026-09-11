using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Counts from the background job runner. Abstracted because the counts live in Hangfire rather
/// than in the schema, and the Application layer does not know Hangfire exists.
/// </summary>
public interface IBackgroundJobStats
{
    /// <summary>Queue depth and outcomes right now, or null when the runner can't be reached.</summary>
    JobQueueStats? Current();
}

/// <summary>
/// What is actually happening across the whole instance. All of it was already being recorded —
/// failing sources, unreadable links, enrichment backlog — and none of it had a view.
/// </summary>
public interface IAdminOverviewService
{
    Task<AdminOverviewResponse> GetAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AdminOverviewService(IAppDbContext db, IBackgroundJobStats jobs) : IAdminOverviewService
{
    /// <summary>Rows in each "worth looking at" list. A console, not a report.</summary>
    private const int ListSize = 10;

    /// <summary>How far back the error and run lists look.</summary>
    private const int RecentDays = 7;

    public async Task<AdminOverviewResponse> GetAsync(CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-RecentDays);

        var failingSources = await db.Sources
            .Where(s => s.Status == SourceStatus.Failing || s.ConsecutiveFailures > 0)
            .OrderByDescending(s => s.ConsecutiveFailures)
            .ThenByDescending(s => s.LastRunAt)
            .Take(ListSize)
            .Select(s => new AdminFailingSource(
                s.Id,
                s.Name,
                db.Users.Where(u => u.Id == s.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                s.ConsecutiveFailures,
                s.Status,
                s.LastRunAt,
                db.SourceRuns
                    .Where(r => r.SourceId == s.Id && r.Status == SourceRunStatus.Failed)
                    .OrderByDescending(r => r.CreationTime)
                    .Select(r => r.Error)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        // Most-fetched hosts: where the outbound traffic actually goes, which is what matters
        // when a site starts refusing us.
        // Ordered on the grouping, then mapped: EF cannot order on a type the query has just
        // constructed.
        var hostRows = await db.Links
            .GroupBy(l => l.Host!.Hostname)
            .Select(g => new
            {
                Hostname = g.Key,
                LinkCount = g.Count(),
                FailedCount = g.Count(l => l.EnrichmentStatus == EnrichmentStatus.Failed
                    || l.EnrichmentStatus == EnrichmentStatus.Broken),
            })
            .OrderByDescending(h => h.LinkCount)
            .Take(ListSize)
            .ToListAsync(ct);

        var topHosts = hostRows
            .Select(h => new AdminHostVolume(h.Hostname, h.LinkCount, h.FailedCount))
            .ToList();

        var recentErrors = await db.Links
            .Where(l => l.EnrichmentError != null && l.LastCheckedAt >= since)
            .OrderByDescending(l => l.LastCheckedAt)
            .Take(ListSize)
            .Select(l => new AdminLinkError(
                l.Id, l.CanonicalUrl, l.EnrichmentStatus, l.EnrichmentError!, l.FailureCount, l.LastCheckedAt))
            .ToListAsync(ct);

        return new AdminOverviewResponse(
            await db.Users.CountAsync(ct),
            await db.Playlists.CountAsync(ct),
            await db.Links.CountAsync(ct),
            await db.PlaylistItems.CountAsync(ct),
            // The enrichment backlog, which is what makes a playlist appear to fill up slowly.
            await db.Links.CountAsync(l => l.EnrichedAt == null, ct),
            await db.Links.CountAsync(l => l.EnrichmentStatus == EnrichmentStatus.Broken, ct),
            await db.Sources.CountAsync(ct),
            await db.Sources.CountAsync(s => s.Status == SourceStatus.Failing, ct),
            await db.SourceRuns.CountAsync(r => r.CreationTime >= since, ct),
            await db.SourceRuns.CountAsync(r => r.CreationTime >= since && r.Status == SourceRunStatus.Failed, ct),
            RecentDays,
            jobs.Current(),
            failingSources,
            topHosts,
            recentErrors);
    }
}
