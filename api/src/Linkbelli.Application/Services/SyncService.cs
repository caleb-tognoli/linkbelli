using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// What changed since a client last looked. Without this a caching client — the extension, an
/// offline queue, a mobile app — has only two options: re-read everything, or trust a stale copy.
/// </summary>
public interface ISyncService
{
    /// <summary>Rows of each kind per call, so one very busy account can't produce an unbounded page.</summary>
    const int MaxRows = 500;

    Task<SyncResponse> ChangesAsync(Guid ownerId, DateTimeOffset? since, CancellationToken ct = default);
}

/// <inheritdoc />
public class SyncService(IAppDbContext db) : ISyncService
{
    public async Task<SyncResponse> ChangesAsync(
        Guid ownerId, DateTimeOffset? since, CancellationToken ct = default)
    {
        // Read from the server's clock, not the caller's: a client with a skewed clock would
        // otherwise ask for a window that skips changes it never saw.
        var until = DateTimeOffset.UtcNow;
        var from = since ?? DateTimeOffset.MinValue;

        // IgnoreQueryFilters on purpose: a deleted row is exactly what a syncing client needs to
        // hear about, and the soft-delete filter would hide the very thing it came for.
        var playlists = await db.Playlists
            .IgnoreQueryFilters()
            .Where(p => p.OwnerId == ownerId && p.LastModified > from && p.LastModified <= until)
            .OrderBy(p => p.LastModified).ThenBy(p => p.Id)
            .Take(ISyncService.MaxRows + 1)
            .Select(p => new SyncedPlaylist(
                p.Id,
                p.DeletionTime != null,
                p.DeletionTime != null ? null : p.Name,
                p.DeletionTime != null ? null : p.Slug,
                p.DeletionTime != null ? null : p.Description,
                p.DeletionTime != null ? null : p.Visibility.ToString(),
                p.DeletionTime != null ? null : p.Tags.Select(t => t.Tag!.Name).ToArray(),
                p.LastModified))
            .ToListAsync(ct);

        var items = await db.PlaylistItems
            .IgnoreQueryFilters()
            .Where(i => db.Playlists.IgnoreQueryFilters().Any(p => p.Id == i.PlaylistId && p.OwnerId == ownerId)
                && i.LastModified > from
                && i.LastModified <= until)
            .OrderBy(i => i.LastModified).ThenBy(i => i.Id)
            .Take(ISyncService.MaxRows + 1)
            .Select(i => new SyncedItem(
                i.Id,
                i.PlaylistId,
                i.DeletionTime != null,
                i.DeletionTime != null ? null : i.Link!.CanonicalUrl,
                i.DeletionTime != null ? null : i.Link!.Title,
                i.DeletionTime != null ? null : i.Note,
                i.DeletionTime != null ? null : i.Status.ToString(),
                i.DeletionTime != null ? null : i.Score,
                i.DeletionTime != null ? null : i.Tags.Select(t => t.Tag!.Name).ToArray(),
                i.LastModified))
            .ToListAsync(ct);

        var more = playlists.Count > ISyncService.MaxRows || items.Count > ISyncService.MaxRows;
        if (playlists.Count > ISyncService.MaxRows) playlists.RemoveAt(ISyncService.MaxRows);
        if (items.Count > ISyncService.MaxRows) items.RemoveAt(ISyncService.MaxRows);

        // When the page was capped, the next window has to resume at the last row actually
        // returned — advancing to `until` would skip whatever was left behind.
        var resumeFrom = more
            ? Earliest(playlists.LastOrDefault()?.LastModified, items.LastOrDefault()?.LastModified) ?? until
            : until;

        return new SyncResponse(resumeFrom, more, playlists, items);
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? left, DateTimeOffset? right) =>
        left is null ? right
        : right is null ? left
        : left < right ? left : right;
}
