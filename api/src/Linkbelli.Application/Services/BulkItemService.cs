using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Applies one action to a selection of items. Each of these already existed per item; what was
/// missing was doing it to forty of them without forty round trips.
/// </summary>
public interface IBulkItemService
{
    /// <summary>Most items one request may touch.</summary>
    const int MaxItems = 500;

    Task<BulkItemResult> ApplyAsync(Guid ownerId, BulkItemRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public class BulkItemService(IAppDbContext db) : IBulkItemService
{
    public async Task<BulkItemResult> ApplyAsync(Guid ownerId, BulkItemRequest request, CancellationToken ct = default)
    {
        if (request.ItemIds.Length == 0)
        {
            return new BulkItemResult(0, 0);
        }

        if (request.ItemIds.Length > IBulkItemService.MaxItems)
        {
            throw new ValidationException("itemIds",
                $"A single request can act on at most {IBulkItemService.MaxItems} items.");
        }

        var ids = request.ItemIds.Distinct().ToList();

        // Ownership is resolved once, for the whole selection. Anything the caller doesn't own is
        // counted as skipped rather than failing the batch — a stale id in a selection is a
        // normal thing to happen, not an error worth throwing the other 39 items away over.
        var items = await db.PlaylistItems
            .Where(i => ids.Contains(i.Id) && i.Playlist!.OwnerId == ownerId)
            // Ordered by where they sit now, so a move or copy lands them in the target in the
            // same relative order. Unordered, the database returns them however it likes and a
            // selection arrives scrambled.
            .OrderBy(i => i.PlaylistId).ThenBy(i => i.Position)
            .ToListAsync(ct);

        var skipped = ids.Count - items.Count;

        if (items.Count == 0)
        {
            return new BulkItemResult(0, skipped);
        }

        var affected = request.Action switch
        {
            BulkAction.Delete => Delete(items),
            BulkAction.SetStatus => SetStatus(items, request),
            BulkAction.SetScore => SetScore(items, request),
            BulkAction.Move or BulkAction.Copy => await MoveOrCopyAsync(ownerId, items, request, ct),
            _ => throw new ValidationException("action", "Unknown action."),
        };

        // Move and copy report their own skips (links already in the target).
        if (request.Action is BulkAction.Move or BulkAction.Copy)
        {
            skipped += items.Count - affected;
        }

        await db.SaveChangesAsync(ct);

        return new BulkItemResult(affected, skipped);
    }

    private int Delete(List<PlaylistItem> items)
    {
        db.PlaylistItems.RemoveRange(items); // soft delete, so the trash can undo it
        return items.Count;
    }

    private static int SetStatus(List<PlaylistItem> items, BulkItemRequest request)
    {
        if (request.Status is not { } status)
        {
            throw new ValidationException("status", "A status is required for this action.");
        }

        var now = DateTimeOffset.UtcNow;
        var changed = 0;

        foreach (var item in items.Where(i => i.Status != status))
        {
            item.Status = status;
            item.StatusChangedAt = now;
            changed++;
        }

        // Items already in that state aren't counted: nothing happened to them, and stamping a
        // fresh timestamp would make re-marking look like progress.
        return changed;
    }

    private static int SetScore(List<PlaylistItem> items, BulkItemRequest request)
    {
        if (request.Score is { } score && score is < 0 or > 100)
        {
            throw new ValidationException("score", "Score must be between 0 and 100.");
        }

        foreach (var item in items)
        {
            item.Score = request.Score;
        }

        return items.Count;
    }

    private async Task<int> MoveOrCopyAsync(
        Guid ownerId, List<PlaylistItem> items, BulkItemRequest request, CancellationToken ct)
    {
        if (request.TargetPlaylistId is not { } targetId)
        {
            throw new ValidationException("targetPlaylistId", "A target playlist is required for this action.");
        }

        if (!await db.Playlists.AnyAsync(p => p.Id == targetId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Target playlist not found.");
        }

        var linkIds = items.Select(i => i.LinkId).Distinct().ToList();

        // The target may already hold some of these links; that's dedup working, not a failure.
        var present = (await db.PlaylistItems
                .Where(i => i.PlaylistId == targetId && linkIds.Contains(i.LinkId))
                .Select(i => i.LinkId)
                .ToListAsync(ct))
            .ToHashSet();

        var nextPosition = await db.PlaylistItems
            .Where(i => i.PlaylistId == targetId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0;

        var moved = 0;
        foreach (var item in items)
        {
            if (item.PlaylistId == targetId || !present.Add(item.LinkId))
            {
                continue;
            }

            nextPosition += PlaylistOrdering.Gap;

            if (request.Action == BulkAction.Move)
            {
                item.PlaylistId = targetId;
                item.Position = nextPosition;
            }
            else
            {
                // A copy carries the note and score across — they are the reader's own work, and
                // leaving them behind would make the copy a worse version of the same link.
                db.PlaylistItems.Add(new PlaylistItem
                {
                    PlaylistId = targetId,
                    LinkId = item.LinkId,
                    Position = nextPosition,
                    Note = item.Note,
                    Score = item.Score,
                    SourceId = item.SourceId,
                    Metadata = item.Metadata is null ? null : new Dictionary<string, string>(item.Metadata),
                    // The person doing the copying, not whoever added the row it came from:
                    // putting a link in this playlist is this person's act.
                    AddedByUserId = ownerId,
                });
            }

            moved++;
        }

        return moved;
    }
}
