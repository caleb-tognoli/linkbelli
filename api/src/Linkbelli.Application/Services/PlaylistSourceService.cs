using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>Which sources feed a playlist, and pulling in what a source found before it was attached.</summary>
public interface IPlaylistSourceService
{
    /// <summary>Attach a source (the caller's own, or any shared one) to a playlist the caller owns. Returns the total number of items ever discovered by the source (for backfill prompt).</summary>
    Task<int> SubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Add all links ever discovered by a source into a playlist the caller owns. Returns count of items added.</summary>
    Task<int> BackfillFromSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Detach a source from a playlist the caller owns.</summary>
    Task UnsubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Sources currently attached to a playlist the caller owns.</summary>
    Task<IReadOnlyList<AttachedSourceSummary>> ListAttachedSourcesAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Shared sources attached to a public (non-private) playlist — safe for anonymous callers.</summary>
    Task<IReadOnlyList<AttachedSourceSummary>> ListPublicAttachedSourcesAsync(string username, string slug, CancellationToken ct = default);
}

/// <inheritdoc />
public class PlaylistSourceService(IAppDbContext db) : IPlaylistSourceService
{
    public async Task<int> SubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default)
    {
        _ = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        // Own sources may be attached regardless of visibility; others' only if shared.
        if (source is null || (source.OwnerId != ownerId && source.Visibility != SourceVisibility.Shared))
        {
            throw new NotFoundException("Source not found.");
        }

        var alreadyAttached = await db.PlaylistSources
            .AnyAsync(ps => ps.PlaylistId == playlistId && ps.SourceId == sourceId, ct);
        if (!alreadyAttached)
        {
            db.PlaylistSources.Add(new PlaylistSource { PlaylistId = playlistId, SourceId = sourceId });
            await db.SaveChangesAsync(ct);
        }

        return await BackfillableLinkIds(sourceId).CountAsync(ct);
    }

    /// <summary>
    /// Every link this source has ever put somewhere, as ids.
    /// </summary>
    /// <remarks>
    /// Read from the items the source created, which is the durable record of what it produced.
    /// This used to read SourceRun.ItemsAdded and describe it as "all distinct URLs ever
    /// discovered", which it is not, three times over: it holds at most SampleSize URLs per run,
    /// it holds only the ones that were new to the whole application at the time — so anything
    /// another account had already saved was missing — and SourceRunRetention prunes old runs, so
    /// the answer shrank over time. A source that had been running a month honestly reported a
    /// handful of links it could restore out of thousands.
    ///
    /// Deliberately not scoped to one playlist: the point of a backfill is to recover what the
    /// source found, wherever it happened to land.
    /// </remarks>
    private IQueryable<Guid> BackfillableLinkIds(Guid sourceId) =>
        db.PlaylistItems
            .IgnoreQueryFilters()
            .Where(i => i.SourceId == sourceId)
            .Select(i => i.LinkId)
            .Distinct();

    public async Task<int> BackfillFromSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default)
    {
        _ = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, ct);
        if (source is null || (source.OwnerId != ownerId && source.Visibility != SourceVisibility.Shared))
            throw new NotFoundException("Source not found.");

        // What is missing here, asked as one question rather than by pulling both sets back and
        // subtracting them in memory. The old shape also matched links by CanonicalUrl, which has
        // no index — a sequential scan over the widest table in the schema.
        var toAdd = await BackfillableLinkIds(sourceId)
            .Where(id => !db.PlaylistItems.Any(i => i.PlaylistId == playlistId && i.LinkId == id))
            .ToListAsync(ct);

        if (toAdd.Count == 0) return 0;

        var maxPos = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0;

        for (var k = 0; k < toAdd.Count; k++)
        {
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlistId,
                LinkId = toAdd[k],
                Position = maxPos + (k + 1) * PlaylistOrdering.Gap,
                Status = PlaylistItemStatus.Added,
                SourceId = sourceId
            });
        }

        await db.SaveChangesAsync(ct);
        return toAdd.Count;
    }

    public async Task UnsubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default)
    {
        _ = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        var attachments = await db.PlaylistSources
            .Where(ps => ps.PlaylistId == playlistId && ps.SourceId == sourceId)
            .ToListAsync(ct);
        if (attachments.Count > 0)
        {
            db.PlaylistSources.RemoveRange(attachments);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<AttachedSourceSummary>> ListAttachedSourcesAsync(
        Guid ownerId, Guid playlistId, CancellationToken ct = default)
    {
        _ = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        return await (from ps in db.PlaylistSources
                      where ps.PlaylistId == playlistId
                      join s in db.Sources on ps.SourceId equals s.Id
                      join u in db.Users on s.OwnerId equals u.Id
                      orderby s.Name
                      select new AttachedSourceSummary(
                          s.Id, s.Name, s.Type, u.UserName!, s.Visibility, s.OwnerId == ownerId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AttachedSourceSummary>> ListPublicAttachedSourcesAsync(
        string username, string slug, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Playlist not found.");

        return await (from ps in db.PlaylistSources
                      where ps.PlaylistId == playlist.Id
                      join s in db.Sources on ps.SourceId equals s.Id
                      where s.Visibility == SourceVisibility.Shared
                      join u in db.Users on s.OwnerId equals u.Id
                      orderby s.Name
                      select new AttachedSourceSummary(
                          s.Id, s.Name, s.Type, u.UserName!, s.Visibility, false))
            .ToListAsync(ct);
    }
}
