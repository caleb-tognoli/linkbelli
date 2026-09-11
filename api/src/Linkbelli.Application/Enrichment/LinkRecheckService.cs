using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

/// <inheritdoc />
public class LinkRecheckService(
    IAppDbContext db,
    ILinkEnrichmentQueue queue,
    ILogger<LinkRecheckService> logger) : ILinkRecheckService
{
    public async Task<int> SweepAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var staleCutoff = now.AddDays(-ILinkRecheckService.FreshDays);

        // Two different kinds of "due": a success that has aged out, and a failure whose backoff
        // has elapsed. Backoff doubles per attempt (6h, 12h, 24h…) so a host that is down stops
        // being asked every few minutes, and gives up entirely after MaxFailures.
        var candidates = await db.Links
            .Where(l =>
                (l.EnrichmentStatus == EnrichmentStatus.Succeeded
                    && (l.LastCheckedAt == null || l.LastCheckedAt < staleCutoff))
                || (l.EnrichmentStatus == EnrichmentStatus.Failed
                    && l.FailureCount < ILinkRecheckService.MaxFailures
                    && l.LastCheckedAt != null))
            .Select(l => new { l.Id, l.EnrichmentStatus, l.LastCheckedAt, l.FailureCount })
            .OrderBy(l => l.LastCheckedAt)
            .Take(ILinkRecheckService.BatchSize * 4)
            .ToListAsync(ct);

        var due = candidates
            .Where(l => l.EnrichmentStatus == EnrichmentStatus.Succeeded || IsPastBackoff(l.LastCheckedAt!.Value, l.FailureCount, now))
            .Take(ILinkRecheckService.BatchSize)
            .ToList();

        foreach (var link in due)
        {
            queue.Enqueue(link.Id);
        }

        if (due.Count > 0)
        {
            logger.LogInformation("Queued {Count} links for re-check.", due.Count);
        }

        return due.Count;
    }

    public async Task RecheckAsync(Guid linkId, CancellationToken ct = default)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == linkId, ct)
            ?? throw new NotFoundException("Link not found.");

        // An explicit retry means someone believes the situation changed. Clear the streak so the
        // backoff it was serving doesn't silently swallow the request.
        link.FailureCount = 0;
        await db.SaveChangesAsync(ct);

        queue.Enqueue(linkId);
    }

    /// <summary>
    /// Whether a failed link has waited long enough. Doubling from FirstRetryHours, capped so the
    /// arithmetic can't overflow into a date far enough out to never come.
    /// </summary>
    private static bool IsPastBackoff(DateTimeOffset lastChecked, int failureCount, DateTimeOffset now)
    {
        var attempts = Math.Clamp(failureCount, 1, ILinkRecheckService.MaxFailures);
        var delay = TimeSpan.FromHours(ILinkRecheckService.FirstRetryHours * Math.Pow(2, attempts - 1));

        return lastChecked + delay <= now;
    }
}
