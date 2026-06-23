using System.Linq.Expressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class PlaylistItemService(IAppDbContext db, ILinkService links, IUserPreferenceService prefs) : IPlaylistItemService
{
    private static readonly Expression<Func<PlaylistItem, PlaylistItemResponse>> ToResponse = i =>
        new PlaylistItemResponse(
            i.Id, i.Position, i.Note, i.Status,
            new LinkResponse(
                i.Link!.Id, i.Link.CanonicalUrl, i.Link.Host!.Hostname, i.Link.Title,
                i.Link.Description, i.Link.ThumbnailUrl, i.Link.SiteName, i.Link.EnrichedAt != null, i.Link.Nsfw),
            i.CreationTime,
            i.Metadata,
            i.SourceId,
            i.Score);

    public async Task<PagedResult<PlaylistItemResponse>> ListAsync(
        Guid ownerId, Guid playlistId, int? limit, string? cursor, string? sort, string? source, CancellationToken ct = default)
    {
        await EnsureOwnsPlaylistAsync(playlistId, ownerId, ct);

        var take = Math.Clamp(limit ?? 50, 1, 100);
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);
        var query = db.PlaylistItems.Where(i => i.PlaylistId == playlistId && i.Link!.EnrichedAt != null);
        if (!showNsfw) query = query.Where(i => !i.Link!.Nsfw);
        query = ApplySourceFilter(query, source);

        return await PageAsync(query, take, cursor, sort, db, ct);
    }

    public async Task<PlaylistItemResponse> AddAsync(
        Guid ownerId, Guid playlistId, AddItemRequest request, CancellationToken ct = default)
    {
        await EnsureOwnsPlaylistAsync(playlistId, ownerId, ct);

        if (!UrlCanonicalizer.TryCanonicalize(request.Url, out var canonical))
        {
            throw new ValidationException("url", "A valid http(s) URL is required.");
        }

        var link = await links.GetOrCreateAsync(canonical, immediate: true, ct);

        if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == playlistId && i.LinkId == link.Id, ct))
        {
            throw new ConflictException("This link is already in the playlist.");
        }

        var maxPos = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0;

        var item = new PlaylistItem
        {
            PlaylistId = playlistId,
            LinkId = link.Id,
            Position = maxPos + PlaylistOrdering.Gap,
            Note = request.Note?.Trim(),
            Status = PlaylistItemStatus.Added,
        };
        db.PlaylistItems.Add(item);
        await db.SaveChangesAsync(ct);

        return await ProjectAsync(item.Id, ct);
    }

    public async Task<PlaylistItemResponse> UpdateAsync(
        Guid ownerId, Guid itemId, UpdateItemRequest request, CancellationToken ct = default)
    {
        var item = await FindOwnedItemAsync(itemId, ownerId, ct);

        if (request.Note is not null)
        {
            item.Note = request.Note.Trim();
        }

        if (request.Status is not null)
        {
            item.Status = request.Status.Value;
        }

        await db.SaveChangesAsync(ct);
        return await ProjectAsync(itemId, ct);
    }

    public async Task<PlaylistItemResponse> SetScoreAsync(Guid ownerId, Guid itemId, int? score, CancellationToken ct = default)
    {
        var item = await FindOwnedItemAsync(itemId, ownerId, ct);
        item.Score = score;
        await db.SaveChangesAsync(ct);
        return await ProjectAsync(itemId, ct);
    }

    public async Task DeleteAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await FindOwnedItemAsync(itemId, ownerId, ct);
        db.PlaylistItems.Remove(item); // soft delete
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlaylistItemResponse> MoveAsync(
        Guid ownerId, Guid itemId, MoveItemRequest request, CancellationToken ct = default)
    {
        var moved = await FindOwnedItemAsync(itemId, ownerId, ct);

        var all = await db.PlaylistItems.Where(i => i.PlaylistId == moved.PlaylistId)
            .OrderBy(i => i.Position).ToListAsync(ct);
        var others = all.Where(i => i.Id != itemId).ToList();

        int insertIndex;
        if (request.AfterItemId is null)
        {
            insertIndex = 0;
        }
        else
        {
            var idx = others.FindIndex(i => i.Id == request.AfterItemId.Value);
            if (idx < 0)
            {
                throw new ValidationException("afterItemId", "Target item is not in this playlist.");
            }

            insertIndex = idx + 1;
        }

        long? before = insertIndex - 1 >= 0 ? others[insertIndex - 1].Position : null;
        long? after = insertIndex < others.Count ? others[insertIndex].Position : null;
        var newPosition = PlaylistOrdering.Between(before, after);

        if (newPosition is null)
        {
            // No room — renumber the whole playlist with the item in its target slot.
            others.Insert(insertIndex, moved);
            for (var k = 0; k < others.Count; k++)
            {
                others[k].Position = (k + 1) * PlaylistOrdering.Gap;
            }
        }
        else
        {
            moved.Position = newPosition.Value;
        }

        await db.SaveChangesAsync(ct);
        return await ProjectAsync(itemId, ct);
    }

    public async Task<PagedResult<PlaylistItemResponse>> ListPublicAsync(
        string username, string slug, int? limit, string? cursor, string? sort, string? source, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new { p.Id, Nsfw = p.Items.Any(i => i.Link!.Nsfw) })
            .FirstOrDefaultAsync(ct);

        var showNsfw = await prefs.ShowNsfwAsync(viewerId, ct);
        if (playlist is null || (playlist.Nsfw && !showNsfw))
        {
            throw new NotFoundException("Playlist not found.");
        }

        var take = Math.Clamp(limit ?? 50, 1, 100);
        var query = db.PlaylistItems.Where(i => i.PlaylistId == playlist.Id && i.Link!.EnrichedAt != null);
        if (!showNsfw) query = query.Where(i => !i.Link!.Nsfw);
        query = ApplySourceFilter(query, source);

        return await PageAsync(query, take, cursor, sort, db, ct);
    }

    private static IQueryable<PlaylistItem> ApplySourceFilter(IQueryable<PlaylistItem> query, string? source)
    {
        if (source == "manual") return query.Where(i => i.SourceId == null);
        if (Guid.TryParse(source, out var sourceId)) return query.Where(i => i.SourceId == sourceId);
        return query;
    }

    private static async Task<PagedResult<PlaylistItemResponse>> PageAsync(
        IQueryable<PlaylistItem> query, int take, string? cursor, string? sort, IAppDbContext db, CancellationToken ct)
    {
        if (sort == "shuffle")
        {
            double seed;
            int offset;

            if (Cursor.TryDecode(cursor, out var v))
            {
                var sep = v.IndexOf(':');
                if (sep > 0
                    && double.TryParse(v[..sep], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out seed)
                    && int.TryParse(v[(sep + 1)..], out offset))
                {
                    // Restore seed+offset from cursor
                }
                else { seed = NewSeed(); offset = 0; }
            }
            else { seed = NewSeed(); offset = 0; }

            // setseed() and ORDER BY random() must run in the same PG session.
            // The transaction pins the connection; setseed is session-state, not rolled back.
            await using var tx = await db.BeginTransactionAsync(ct);
            await db.SeedRandomAsync(seed, ct);

            var rows = await query
                .OrderBy(_ => EF.Functions.Random())
                .Skip(offset)
                .Take(take + 1)
                .Select(ToResponse)
                .ToListAsync(ct);

            await tx.CommitAsync(ct);

            string? next = null;
            if (rows.Count > take)
            {
                rows.RemoveAt(take);
                var seedStr = seed.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                next = Cursor.Encode($"{seedStr}:{offset + take}");
            }
            return new PagedResult<PlaylistItemResponse>(rows, next);
        }

        if (sort is "date-asc" or "date-desc")
        {
            bool asc = sort == "date-asc";
            if (Cursor.TryDecode(cursor, out var v) && long.TryParse(v, out var ticks))
            {
                var t = new DateTimeOffset(ticks, TimeSpan.Zero);
                query = asc ? query.Where(i => i.CreationTime > t) : query.Where(i => i.CreationTime < t);
            }
            var rows = await (asc
                ? query.OrderBy(i => i.CreationTime)
                : query.OrderByDescending(i => i.CreationTime))
                .Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.Encode(rows[^1].CreationTime.UtcTicks.ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next);
        }

        if (sort is "score-asc" or "score-desc")
        {
            bool asc = sort == "score-asc";
            int offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? o : 0;
            // NULL scores always sort last regardless of direction.
            IQueryable<PlaylistItem> q = asc
                ? query.OrderBy(i => i.Score == null ? 1 : 0).ThenBy(i => i.Score).ThenBy(i => i.Position)
                : query.OrderBy(i => i.Score == null ? 1 : 0).ThenByDescending(i => i.Score).ThenBy(i => i.Position);
            var rows = await q.Skip(offset).Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.Encode((offset + take).ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next);
        }

        else
        {
            if (Cursor.TryDecode(cursor, out var v) && long.TryParse(v, out var afterPos))
                query = query.Where(i => i.Position > afterPos);
            var rows = await query.OrderBy(i => i.Position).Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.Encode(rows[^1].Position.ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next);
        }
    }

    private static double NewSeed() => Random.Shared.NextDouble() * 2.0 - 1.0;

    private Task<PlaylistItemResponse> ProjectAsync(Guid itemId, CancellationToken ct) =>
        db.PlaylistItems.Where(i => i.Id == itemId).Select(ToResponse).FirstAsync(ct);

    private async Task EnsureOwnsPlaylistAsync(Guid playlistId, Guid ownerId, CancellationToken ct)
    {
        if (!await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }
    }

    private async Task<PlaylistItem> FindOwnedItemAsync(Guid itemId, Guid ownerId, CancellationToken ct) =>
        await db.PlaylistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.Playlist!.OwnerId == ownerId, ct)
        ?? throw new NotFoundException("Item not found.");
}
