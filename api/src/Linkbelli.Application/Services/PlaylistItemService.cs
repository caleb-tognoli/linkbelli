using System.Linq.Expressions;
using Linkbelli.Application.Automation;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Webhooks;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Tags;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class PlaylistItemService(
    IAppDbContext db,
    ILinkService links,
    IUserPreferenceService prefs,
    ITagResolver tags,
    IAutomationRunner automation,
    IPlaylistAccess access,
    IWebhookEvents webhooks) : IPlaylistItemService
{
    /// <summary>
    /// One item as the API returns it.
    /// </summary>
    /// <remarks>
    /// An instance field rather than a static one because the adder's name comes from a
    /// correlated subquery: PlaylistItem lives in Core, which does not know ApplicationUser
    /// exists, so there is no navigation to follow. Same shape PlaylistService uses for an
    /// owner's username.
    /// </remarks>
    private Expression<Func<PlaylistItem, PlaylistItemResponse>> ToResponse => i =>
        new PlaylistItemResponse(
            i.Id, i.Position, i.Note, i.Status,
            new LinkResponse(
                i.Link!.Id, i.Link.CanonicalUrl, i.Link.Host!.Hostname, i.Link.Title,
                i.Link.Description, i.Link.ThumbnailUrl, i.Link.SiteName, i.Link.EnrichedAt != null, i.Link.Nsfw,
                i.Link.Host.Favicon, i.Link.EnrichmentStatus, i.Link.EnrichmentError, i.Link.WordCount, i.Link.Kind,
                i.Link.ArchiveUrl),
            i.CreationTime,
            i.Metadata,
            i.SourceId,
            i.Score,
            i.StatusChangedAt,
            i.Tags.Select(t => t.Tag!.Name).ToArray(),
            i.ShareToken,
            // The name rather than the id: this is rendered next to a row, and a client should
            // not have to resolve a guid to draw it. Whether it is worth showing — it is not, on
            // a playlist only one person touches — is the caller's decision, not this one's.
            db.Users.Where(u => u.Id == i.AddedByUserId).Select(u => u.UserName).FirstOrDefault(),
            i.ReadProgress,
            i.LastReadAt,
            i.SnoozedUntil,
            i.SnoozeCount);

    public async Task<PagedResult<PlaylistItemResponse>> ListAsync(
        Guid ownerId, Guid playlistId, int? limit, string? cursor, string? sort, string? source, string? status, string? q, CancellationToken ct = default)
    {
        await EnsureCanReadAsync(playlistId, ownerId, ct);

        var take = Paging.Take(limit);
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);
        var query = db.PlaylistItems.Where(i => i.PlaylistId == playlistId && i.Link!.EnrichedAt != null);
        if (!showNsfw) query = query.Where(i => !i.Link!.Nsfw);
        query = ApplySourceFilter(query, source);
        query = ApplyStatusFilter(query, status);
        query = ApplyQueryFilter(query, q);

        return await PageAsync(query, take, cursor, sort, q, db, ct);
    }

    public async Task<PlaylistItemResponse> AddAsync(
        Guid ownerId, Guid playlistId, AddItemRequest request, CancellationToken ct = default)
    {
        await EnsureOwnsPlaylistAsync(playlistId, ownerId, ct);

        if (!UrlCanonicalizer.TryCanonicalize(request.Url, out var canonical))
        {
            throw new ValidationException("url", "A valid http(s) URL is required.");
        }

        var link = await links.GetOrCreateAsync(canonical, immediate: true, cancellationToken: ct);

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
            AddedByUserId = ownerId,
        };
        db.PlaylistItems.Add(item);
        await db.SaveChangesAsync(ct);

        // Before the rules run, so a receiver hears where it was saved; anything a rule then
        // does to it is an event of its own.
        await webhooks.ItemsAddedAsync([item.Id], ItemOrigin.Manual, ct);

        // Straight away for a manual add, where the person is watching: a rule that files their
        // link a minute after they saved it looks like the app moved it on its own. The sweep is
        // what makes the rules reliable; this is what makes them feel immediate.
        await automation.ApplyAsync([item.Id], ct);

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

        var finished = false;
        if (request.Status is not null && request.Status.Value != item.Status)
        {
            item.Status = request.Status.Value;
            item.StatusChangedAt = DateTimeOffset.UtcNow;
            finished = item.Status == PlaylistItemStatus.Watched;
        }

        List<string> tagged = [];

        if (request.Tags is not null)
        {
            // Replaces the whole set, the same way playlist tags do — a client sends the tags it
            // wants rather than a patch, so removing one doesn't need its own verb.
            var resolved = await tags.ResolveAsync(TagNormalizer.Normalize(request.Tags), ct);
            var existing = await db.PlaylistItemTags.Where(t => t.PlaylistItemId == itemId).ToListAsync(ct);
            db.PlaylistItemTags.RemoveRange(existing);

            // Only the ones that are new. Sending the whole set back is how a client removes one,
            // and that should not announce every tag it kept as though it had just been added.
            var had = existing.Select(t => t.TagId).ToHashSet();
            tagged = [.. resolved.Where(t => !had.Contains(t.Id)).Select(t => t.Name)];

            foreach (var tag in resolved)
            {
                db.PlaylistItemTags.Add(new PlaylistItemTag { PlaylistItemId = itemId, TagId = tag.Id });
            }

            // Tags live in their own rows, so changing them leaves the item untouched — and a
            // client syncing on LastModified would never hear about it. Touch the item itself.
            item.LastModified = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        if (finished)
        {
            await webhooks.ItemsFinishedAsync([itemId], ct);
        }

        foreach (var tag in tagged)
        {
            await webhooks.ItemsTaggedAsync([itemId], tag, ct);
        }

        return await ProjectAsync(itemId, ct);
    }

    public async Task<PlaylistItemResponse> SetScoreAsync(Guid ownerId, Guid itemId, int? score, CancellationToken ct = default)
    {
        var item = await FindOwnedItemAsync(itemId, ownerId, ct);
        item.Score = score;
        await db.SaveChangesAsync(ct);
        return await ProjectAsync(itemId, ct);
    }

    /// <summary>
    /// How many times something has to be put aside before it is worth saying so.
    /// </summary>
    /// <remarks>
    /// Three is "you keep meaning to". A client can use the count to offer getting rid of it,
    /// which is kinder than silently re-offering it forever — and kinder than letting somebody
    /// feel guilty about a list they are never going to read.
    /// </remarks>
    public const int SnoozesWorthMentioning = 3;

    public async Task<PlaylistItemResponse> SnoozeAsync(
        Guid ownerId, Guid itemId, DateTimeOffset? until, DateTimeOffset now, CancellationToken ct = default)
    {
        var item = await FindOwnedItemAsync(itemId, ownerId, ct);

        if (until is null)
        {
            // Waking it, not snoozing it. The count stays: how often it has been put aside is
            // still true, and is the thing worth knowing.
            item.SnoozedUntil = null;
            await db.SaveChangesAsync(ct);
            return await ProjectAsync(itemId, ct);
        }

        if (until <= now)
        {
            throw new ValidationException("until", "Pick a moment that has not already happened.");
        }

        // Stored in UTC. The offset matters while a preset is being worked out — "tonight" is
        // the caller's evening — and not at all afterwards, since it is the same instant either
        // way; Npgsql refuses anything else for a timestamptz.
        item.SnoozedUntil = until.Value.ToUniversalTime();
        item.SnoozeCount++;
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

        // Only the two neighbours matter. Materializing the whole playlist to find them meant a
        // 10k-item list was loaded into memory for every single drag.
        long? before;
        long? after;

        if (request.AfterItemId is null)
        {
            // To the front: the only neighbour is whatever currently sits first.
            before = null;
            after = await db.PlaylistItems
                .Where(i => i.PlaylistId == moved.PlaylistId && i.Id != itemId)
                .MinAsync(i => (long?)i.Position, ct);
        }
        else
        {
            before = await db.PlaylistItems
                .Where(i => i.Id == request.AfterItemId.Value
                    && i.PlaylistId == moved.PlaylistId
                    && i.Id != itemId)
                .Select(i => (long?)i.Position)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("afterItemId", "Target item is not in this playlist.");

            after = await db.PlaylistItems
                .Where(i => i.PlaylistId == moved.PlaylistId && i.Id != itemId && i.Position > before)
                .OrderBy(i => i.Position)
                .Select(i => (long?)i.Position)
                .FirstOrDefaultAsync(ct);
        }

        var newPosition = PlaylistOrdering.Between(before, after);
        if (newPosition is not null)
        {
            moved.Position = newPosition.Value;
            await db.SaveChangesAsync(ct);
            return await ProjectAsync(itemId, ct);
        }

        // The neighbours are adjacent integers, so there is nowhere to land between them. This
        // is the one case that genuinely needs the whole list, and gapped positions make it rare.
        await RenumberAsync(moved, request.AfterItemId, ct);
        return await ProjectAsync(itemId, ct);
    }

    /// <summary>Respaces every item in the playlist, with the moved one in its target slot.</summary>
    private async Task RenumberAsync(PlaylistItem moved, Guid? afterItemId, CancellationToken ct)
    {
        var others = await db.PlaylistItems
            .Where(i => i.PlaylistId == moved.PlaylistId && i.Id != moved.Id)
            .OrderBy(i => i.Position)
            .ToListAsync(ct);

        var insertIndex = afterItemId is null
            ? 0
            : others.FindIndex(i => i.Id == afterItemId.Value) + 1;

        others.Insert(insertIndex, moved);
        for (var k = 0; k < others.Count; k++)
        {
            others[k].Position = (k + 1) * PlaylistOrdering.Gap;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<PlaylistItemResponse>> ListPublicAsync(
        string username, string slug, int? limit, string? cursor, string? sort, string? source, string? status, string? q, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new { p.Id, Nsfw = p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw) })
            .FirstOrDefaultAsync(ct);

        var showNsfw = await prefs.ShowNsfwAsync(viewerId, ct);
        if (playlist is null || (playlist.Nsfw && !showNsfw))
        {
            throw new NotFoundException("Playlist not found.");
        }

        var take = Paging.Take(limit);
        var query = db.PlaylistItems.Where(i => i.PlaylistId == playlist.Id && i.Link!.EnrichedAt != null);
        if (!showNsfw) query = query.Where(i => !i.Link!.Nsfw);
        query = ApplySourceFilter(query, source);
        query = ApplyStatusFilter(query, status);
        query = ApplyQueryFilter(query, q);

        return await PageAsync(query, take, cursor, sort, q, db, ct);
    }

    private static IQueryable<PlaylistItem> ApplyStatusFilter(IQueryable<PlaylistItem> query, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return query;
        if (status.Equals("watched", StringComparison.OrdinalIgnoreCase))
            return query.Where(i => i.Status == PlaylistItemStatus.Watched);
        if (status.Equals("unwatched", StringComparison.OrdinalIgnoreCase))
            return query.Where(i => i.Status == PlaylistItemStatus.Added);
        return query;
    }

    private static IQueryable<PlaylistItem> ApplyQueryFilter(IQueryable<PlaylistItem> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return query;

        // If the query parses as a URL, match by canonical hash (indexed dedup key).
        if (UrlCanonicalizer.TryCanonicalize(q, out var canon))
        {
            var hash = canon.Hash;
            return query.Where(i => i.Link!.UrlHash == hash);
        }

        // lower(col) LIKE '%needle%'. Trigram GIN indexes on those same expressions make this
        // indexable (see the AddSearchIndexes migration); Postgres still falls back to a scan
        // for terms under three characters, which have too little trigram content to match on.
        var needle = q.ToLower();
        return query.Where(i =>
            (i.Link!.Title != null && i.Link.Title.ToLower().Contains(needle))
            || (i.Link!.Description != null && i.Link.Description.ToLower().Contains(needle))
            || (i.Link!.SiteName != null && i.Link.SiteName.ToLower().Contains(needle))
            || i.Link!.CanonicalUrl.ToLower().Contains(needle)
            || i.Link!.Host!.Hostname.ToLower().Contains(needle));
    }

    /// <summary>
    /// Whether a search term should drive the ordering. A term the user typed is a stronger
    /// signal about what they want to see first than the playlist's resting order — but only
    /// when they haven't asked for a specific sort, which is an explicit instruction.
    /// </summary>
    private static bool RanksByRelevance(string? q, string? sort) =>
        !string.IsNullOrWhiteSpace(q) && sort is null or "" or "position";

    /// <summary>
    /// Relevance buckets, best first: a title hit beats a site-name hit beats everything else
    /// (description, URL, hostname). Ties fall back to the playlist's own order.
    /// </summary>
    private static IOrderedQueryable<PlaylistItem> OrderByRelevance(IQueryable<PlaylistItem> query, string q)
    {
        var needle = q.ToLower();
        return query
            .OrderBy(i => i.Link!.Title != null && i.Link.Title.ToLower().Contains(needle) ? 0
                : i.Link!.SiteName != null && i.Link.SiteName.ToLower().Contains(needle) ? 1
                : 2)
            .ThenBy(i => i.Position);
    }

    private static IQueryable<PlaylistItem> ApplySourceFilter(IQueryable<PlaylistItem> query, string? source)
    {
        if (source == "manual") return query.Where(i => i.SourceId == null);
        if (Guid.TryParse(source, out var sourceId)) return query.Where(i => i.SourceId == sourceId);
        return query;
    }

    private async Task<PagedResult<PlaylistItemResponse>> PageAsync(
        IQueryable<PlaylistItem> query, int take, string? cursor, string? sort, string? q, IAppDbContext db, CancellationToken ct)
    {
        // The total is counted once, on the first page, then carried inside the cursor. Counting on
        // every "load more" ran a second full pass over the filtered set — doubling the cost of
        // exactly the queries (filtered, searched, large) that are already the slowest.
        var continuing = Cursor.DecodePage(cursor, out var total, out var payload);
        if (!continuing)
        {
            total = await query.CountAsync(ct);
        }

        if (sort == "shuffle")
        {
            double seed;
            int offset;

            var sep = payload.IndexOf(':');
            if (sep > 0
                && double.TryParse(payload[..sep], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out seed)
                && int.TryParse(payload[(sep + 1)..], out offset)
                && offset >= 0)
            {
                // Seed + offset restored from the cursor: the same shuffle order continues.
            }
            else if (continuing)
            {
                // A shuffle that quietly restarts deals the same links again in a new order, and
                // the reader has no way to tell that from the shuffle simply being like that.
                throw Cursor.Malformed();
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
                next = Cursor.EncodePage(total, $"{seedStr}:{offset + take}");
            }
            return new PagedResult<PlaylistItemResponse>(rows, next) { Total = total };
        }

        if (sort is "date-asc" or "date-desc")
        {
            bool asc = sort == "date-asc";

            // Keyset on (CreationTime, Id), not CreationTime alone. A source run inserts all of
            // its items in one SaveChanges, so they share a creation timestamp to the tick; a
            // cursor that only compared timestamps skipped every tied row after the page break.
            if (continuing)
            {
                var at = Cursor.ParseTimeKey(payload)
                    ?? throw Cursor.Malformed();
                var t = at.At;
                var lastId = at.Id;
                query = asc
                    ? query.Where(i => i.CreationTime > t || (i.CreationTime == t && i.Id.CompareTo(lastId) > 0))
                    : query.Where(i => i.CreationTime < t || (i.CreationTime == t && i.Id.CompareTo(lastId) < 0));
            }

            var rows = await (asc
                ? query.OrderBy(i => i.CreationTime).ThenBy(i => i.Id)
                : query.OrderByDescending(i => i.CreationTime).ThenByDescending(i => i.Id))
                .Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take)
            {
                rows.RemoveAt(take);
                next = Cursor.EncodePage(total, Cursor.FormatTimeKey(rows[^1].CreationTime, rows[^1].Id));
            }
            return new PagedResult<PlaylistItemResponse>(rows, next) { Total = total };
        }

        if (sort is "score-asc" or "score-desc")
        {
            bool asc = sort == "score-asc";
            var offset = Cursor.ParseOffset(payload);
            // NULL scores always sort last regardless of direction.
            IQueryable<PlaylistItem> ordered = asc
                ? query.OrderBy(i => i.Score == null ? 1 : 0).ThenBy(i => i.Score).ThenBy(i => i.Position)
                : query.OrderBy(i => i.Score == null ? 1 : 0).ThenByDescending(i => i.Score).ThenBy(i => i.Position);
            var rows = await ordered.Skip(offset).Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.EncodePage(total, (offset + take).ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next) { Total = total };
        }

        if (RanksByRelevance(q, sort))
        {
            // Offset paging, not a keyset: the relevance bucket isn't a stored column, so there
            // is no cursor value to compare the next page against.
            var offset = Cursor.ParseOffset(payload);
            var rows = await OrderByRelevance(query, q!)
                .Skip(offset).Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.EncodePage(total, (offset + take).ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next) { Total = total };
        }

        else
        {
            if (continuing)
            {
                if (!long.TryParse(payload, out var afterPos))
                {
                    throw Cursor.Malformed();
                }

                query = query.Where(i => i.Position > afterPos);
            }
            var rows = await query.OrderBy(i => i.Position).Take(take + 1).Select(ToResponse).ToListAsync(ct);
            string? next = null;
            if (rows.Count > take) { rows.RemoveAt(take); next = Cursor.EncodePage(total, rows[^1].Position.ToString()); }
            return new PagedResult<PlaylistItemResponse>(rows, next) { Total = total };
        }
    }

    private static double NewSeed() => Random.Shared.NextDouble() * 2.0 - 1.0;

    private Task<PlaylistItemResponse> ProjectAsync(Guid itemId, CancellationToken ct) =>
        db.PlaylistItems.Where(i => i.Id == itemId).Select(ToResponse).FirstAsync(ct);

    /// <summary>
    /// Reading a playlist. A playlist shared with someone is theirs to read, which is the whole
    /// point of sharing it.
    /// </summary>
    private Task EnsureCanReadAsync(Guid playlistId, Guid userId, CancellationToken ct) =>
        access.EnsureAsync(userId, playlistId, PlaylistRole.Viewer, ct);

    /// <summary>Adding to it. A contributor may put things in without being able to take them out.</summary>
    private Task EnsureOwnsPlaylistAsync(Guid playlistId, Guid userId, CancellationToken ct) =>
        access.EnsureAsync(userId, playlistId, PlaylistRole.Contributor, ct);

    /// <summary>
    /// Changing or removing one item. An editor's job; a contributor adds, which is deliberately
    /// not the same permission — "help me collect things" should not also mean "delete things".
    /// </summary>
    private async Task<PlaylistItem> FindOwnedItemAsync(Guid itemId, Guid userId, CancellationToken ct)
    {
        var item = await db.PlaylistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new NotFoundException("Item not found.");

        var role = await access.RoleAsync(userId, item.PlaylistId, ct);
        if (role is not PlaylistRole.Editor)
        {
            throw new NotFoundException("Item not found.");
        }

        return item;
    }
}
