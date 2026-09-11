using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// The size and shape of what someone has here. Quotas were only ever visible as a 429, and
/// nothing reported the collection itself — so there was no way to watch it grow, or to notice
/// a hundred links quietly rotting in it.
/// </summary>
public interface IUsageService
{
    Task<UsageResponse> GetAsync(Guid ownerId, CancellationToken ct = default);
}

/// <inheritdoc />
public class UsageService(IAppDbContext db) : IUsageService
{
    public async Task<UsageResponse> GetAsync(Guid ownerId, CancellationToken ct = default)
    {
        var items = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId);

        // Counted in one round trip rather than nine: this sits on a page someone opens to look
        // at, not a hot path, but nine sequential counts is still nine sequential counts.
        var counts = await db.Playlists
            .Where(p => p.OwnerId == ownerId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Playlists = g.Count(),
                Items = items.Count(i => i.Link!.EnrichedAt != null),
                PendingItems = items.Count(i => i.Link!.EnrichedAt == null),
                Watched = items.Count(i => i.Status == PlaylistItemStatus.Watched),
                Broken = items.Count(i => i.Link!.EnrichmentStatus == EnrichmentStatus.Broken
                    || i.Link.EnrichmentStatus == EnrichmentStatus.Failed),
                Sites = items.Select(i => i.Link!.HostId).Distinct().Count(),
            })
            .FirstOrDefaultAsync(ct);

        var folders = await db.Folders.CountAsync(f => f.OwnerId == ownerId, ct);
        var sources = await db.Sources.CountAsync(s => s.OwnerId == ownerId, ct);
        var savedSearches = await db.SavedSearches.CountAsync(ss => ss.OwnerId == ownerId, ct);

        // Deleted rows are hidden by the global filter, which is exactly what makes them easy to
        // forget about — so they are counted deliberately.
        var trashedPlaylists = await db.Playlists.IgnoreQueryFilters()
            .CountAsync(p => p.OwnerId == ownerId && p.DeletionTime != null, ct);
        var trashedItems = await db.PlaylistItems.IgnoreQueryFilters()
            .CountAsync(i => i.DeletionTime != null
                && db.Playlists.IgnoreQueryFilters()
                    .Any(p => p.Id == i.PlaylistId && p.OwnerId == ownerId && p.DeletionTime == null), ct);

        return new UsageResponse(
            counts?.Playlists ?? 0,
            counts?.Items ?? 0,
            counts?.PendingItems ?? 0,
            folders,
            sources,
            savedSearches,
            counts?.Sites ?? 0,
            counts?.Watched ?? 0,
            counts?.Broken ?? 0,
            trashedPlaylists + trashedItems);
    }
}
