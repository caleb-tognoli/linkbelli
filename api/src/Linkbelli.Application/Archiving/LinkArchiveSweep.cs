using Linkbelli.Application.Data;
using Linkbelli.Application.Observability;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Archiving;

/// <summary>Gets public snapshots kept for the links of people who asked for them.</summary>
public interface ILinkArchiveSweep
{
    /// <summary>Archives a batch. Returns how many snapshots it came away with.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class LinkArchiveSweep(
    IAppDbContext db,
    IArchiver archiver,
    AppMetrics metrics,
    ILogger<LinkArchiveSweep> logger) : ILinkArchiveSweep
{
    /// <summary>
    /// Links per run. Small on purpose: each one is an outbound request to a service that is
    /// doing us a favour, and the sweep runs often enough to get through a backlog anyway.
    /// </summary>
    public const int BatchSize = 10;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var links = await db.Links
            .Where(l => l.ArchiveUrl == null
                && l.EnrichedAt != null
                && l.EnrichmentStatus == EnrichmentStatus.Succeeded
                && l.ArchiveAttempts < Link.MaxArchiveAttempts
                // Someone who saved it asked for it to be archived. Links are shared globally, so
                // one person opting in must not archive another person's reading — but a link
                // they both saved was going to be archived either way.
                && db.PlaylistItems.Any(i => i.LinkId == l.Id
                    && db.Users.Any(u => u.Id == i.Playlist!.OwnerId && u.ArchiveLinks)))
            .OrderBy(l => l.ArchiveAttempts)
            .ThenBy(l => l.CreationTime)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
        {
            return 0;
        }

        var archived = 0;
        foreach (var link in links)
        {
            var result = await archiver.ArchiveAsync(link.CanonicalUrl, cancellationToken);

            metrics.Archive(result.Url is not null ? "archived" : result.Retry ? "deferred" : "refused");

            if (result.Url is not null)
            {
                link.ArchiveUrl = result.Url;
                link.ArchivedAt = DateTimeOffset.UtcNow;
                archived++;
            }
            else if (!result.Retry)
            {
                // Refused rather than deferred: count it, so three of these stop the link being
                // offered up again forever.
                link.ArchiveAttempts++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Archived {Archived} of {Count} links.", archived, links.Count);

        return archived;
    }
}
