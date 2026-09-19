using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Services;

/// <summary>
/// Deleting a playlist or an item stamps DeletionTime and keeps the row. This is what makes that
/// recoverable — and what eventually clears it out, so retained rows don't accumulate forever.
/// </summary>
public class TrashService(IAppDbContext db, IAuditLog audit, ILogger<TrashService> logger) : ITrashService
{
    /// <summary>How long a deleted row can still be restored before it is purged for good.</summary>
    public const int RetentionDays = 30;

    public async Task<TrashResponse> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);

        var playlists = await db.Playlists
            .IgnoreQueryFilters()
            .Where(p => p.OwnerId == ownerId && p.DeletionTime != null && p.DeletionTime > cutoff)
            .OrderByDescending(p => p.DeletionTime)
            .Select(p => new TrashedPlaylist(
                p.Id,
                p.Name,
                p.Slug,
                // Counted without the enrichment filter: this is "what comes back", not "what is shown".
                db.PlaylistItems.IgnoreQueryFilters().Count(i => i.PlaylistId == p.Id && i.DeletionTime == null),
                p.DeletionTime!.Value,
                p.DeletionTime!.Value.AddDays(RetentionDays)))
            .ToListAsync(ct);

        // Items whose playlist is also deleted are left out — they are restored with the playlist,
        // and listing them separately would offer a restore that can't land anywhere. The
        // playlist's own DeletionTime is tested explicitly: IgnoreQueryFilters applies to the
        // whole query tree, so the soft-delete filter is off inside this subquery too.
        var items = await db.PlaylistItems
            .IgnoreQueryFilters()
            .Where(i => i.DeletionTime != null
                && i.DeletionTime > cutoff
                && db.Playlists.Any(p => p.Id == i.PlaylistId
                    && p.OwnerId == ownerId
                    && p.DeletionTime == null))
            .OrderByDescending(i => i.DeletionTime)
            .Select(i => new TrashedItem(
                i.Id,
                i.PlaylistId,
                db.Playlists.Where(p => p.Id == i.PlaylistId && p.DeletionTime == null).Select(p => p.Name).First(),
                i.Link!.CanonicalUrl,
                i.Link.Title,
                i.DeletionTime!.Value,
                i.DeletionTime!.Value.AddDays(RetentionDays)))
            .ToListAsync(ct);

        return new TrashResponse(playlists, items, RetentionDays);
    }

    public async Task RestorePlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default)
    {
        var playlist = await db.Playlists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId && p.DeletionTime != null, ct)
            ?? throw new NotFoundException("No deleted playlist with that id.");

        // The unique (OwnerId, Slug) index only covers live rows, so a new playlist may have
        // taken this one's slug while it sat in the trash. Suffix rather than refuse — the
        // playlist is the thing being recovered; its URL is not worth failing over.
        var slug = playlist.Slug;
        var suffix = 2;
        while (await db.Playlists.AnyAsync(p => p.OwnerId == ownerId && p.Slug == slug, ct))
        {
            slug = $"{playlist.Slug}-{suffix++}";
        }

        playlist.Slug = slug;
        playlist.DeletionTime = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task RestoreItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await db.PlaylistItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Id == itemId
                && i.DeletionTime != null
                && db.Playlists.Any(p => p.Id == i.PlaylistId
                    && p.OwnerId == ownerId
                    && p.DeletionTime == null), ct)
            ?? throw new NotFoundException("No deleted item with that id.");

        // Unlike a slug, a duplicate link in a playlist is exactly what dedup exists to prevent,
        // so a link that came back on its own wins and the restore is refused.
        if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == item.PlaylistId && i.LinkId == item.LinkId, ct))
        {
            throw new ConflictException("That link is already back in the playlist.");
        }

        // Restore to the end rather than to a position that has since been filled.
        item.Position = (await db.PlaylistItems
            .Where(i => i.PlaylistId == item.PlaylistId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0) + PlaylistItem.PositionGap;
        item.DeletionTime = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task<int> EmptyAsync(Guid ownerId, CancellationToken ct = default)
    {
        var removed = await PurgeAsync(
            p => p.OwnerId == ownerId && p.DeletionTime != null,
            i => i.DeletionTime != null && db.Playlists.IgnoreQueryFilters().Any(p => p.Id == i.PlaylistId && p.OwnerId == ownerId),
            ct);

        // The one action in the app that destroys rows outright rather than hiding them, so it
        // is the one most worth having a record of.
        if (removed > 0)
        {
            await audit.RecordAsync(
                ownerId, "trash.empty", summary: $"Permanently removed {removed} rows from the trash.",
                details: new { removed }, ct: ct);
        }

        return removed;
    }

    // One thing at a time, for when the whole trash is not what somebody wants gone: "Empty trash"
    // was the only way to be rid of anything before its thirty days were up.
    public async Task PurgePlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default)
    {
        var removed = await PurgeAsync(
            p => p.Id == playlistId && p.OwnerId == ownerId && p.DeletionTime != null,
            i => false,
            ct);

        if (removed == 0)
        {
            throw new NotFoundException("No deleted playlist with that id.");
        }

        await audit.RecordAsync(
            ownerId, "trash.purge", summary: $"Permanently removed a playlist from the trash ({removed} rows).",
            details: new { playlistId, removed }, ct: ct);
    }

    public async Task PurgeItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var removed = await PurgeAsync(
            p => false,
            i => i.Id == itemId
                && i.DeletionTime != null
                && db.Playlists.IgnoreQueryFilters().Any(p => p.Id == i.PlaylistId && p.OwnerId == ownerId),
            ct);

        if (removed == 0)
        {
            throw new NotFoundException("No deleted item with that id.");
        }

        await audit.RecordAsync(
            ownerId, "trash.purge", summary: "Permanently removed a link from the trash.",
            details: new { itemId }, ct: ct);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        var removed = await PurgeAsync(
            p => p.DeletionTime != null && p.DeletionTime <= cutoff,
            i => i.DeletionTime != null && i.DeletionTime <= cutoff,
            ct);

        if (removed > 0)
        {
            logger.LogInformation("Purged {Count} rows deleted before {Cutoff}.", removed, cutoff);
        }

        return removed;
    }

    /// <summary>
    /// Hard-deletes the matching playlists (with everything hanging off them) and the matching
    /// loose items. Children go first so no foreign key is left dangling.
    /// </summary>
    private async Task<int> PurgeAsync(
        System.Linq.Expressions.Expression<Func<Playlist, bool>> playlistFilter,
        System.Linq.Expressions.Expression<Func<PlaylistItem, bool>> itemFilter,
        CancellationToken ct)
    {
        var doomedPlaylists = await db.Playlists
            .IgnoreQueryFilters()
            .Where(playlistFilter)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var removed = 0;

        if (doomedPlaylists.Count > 0)
        {
            // A purged playlist takes all of its items with it, deleted or not — they have
            // nowhere to be restored to once the playlist is gone.
            removed += await db.PlaylistItems.IgnoreQueryFilters()
                .Where(i => doomedPlaylists.Contains(i.PlaylistId)).ExecuteDeleteAsync(ct);
            removed += await db.PlaylistTags.IgnoreQueryFilters()
                .Where(pt => doomedPlaylists.Contains(pt.PlaylistId)).ExecuteDeleteAsync(ct);
            removed += await db.PlaylistSources.IgnoreQueryFilters()
                .Where(ps => doomedPlaylists.Contains(ps.PlaylistId)).ExecuteDeleteAsync(ct);
            removed += await db.FolderPlaylists.IgnoreQueryFilters()
                .Where(fp => doomedPlaylists.Contains(fp.PlaylistId)).ExecuteDeleteAsync(ct);
            removed += await db.Playlists.IgnoreQueryFilters()
                .Where(p => doomedPlaylists.Contains(p.Id)).ExecuteDeleteAsync(ct);
        }

        removed += await db.PlaylistItems.IgnoreQueryFilters().Where(itemFilter).ExecuteDeleteAsync(ct);

        return removed;
    }
}
