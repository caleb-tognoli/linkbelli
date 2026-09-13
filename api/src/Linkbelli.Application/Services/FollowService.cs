using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Email;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Keeping up with a playlist, or with everything someone publishes — and the feed that makes it
/// worth doing. Saving a public playlist to a folder files a copy of it; it never told anyone
/// that something new had turned up in it.
/// </summary>
public interface IFollowService
{
    Task<FollowStateResponse> FollowPlaylistAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    Task<FollowStateResponse> UnfollowPlaylistAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    Task<FollowStateResponse> FollowUserAsync(Guid userId, string username, CancellationToken ct = default);

    Task<FollowStateResponse> UnfollowUserAsync(Guid userId, string username, CancellationToken ct = default);

    /// <summary>What the caller follows, so a client can show it without asking per row.</summary>
    Task<FollowingResponse> ListFollowingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Links that have turned up in anything the caller follows, newest first, with how many
    /// arrived since they last looked.
    /// </summary>
    Task<FeedResponse> GetFeedAsync(Guid userId, int? limit, string? cursor, CancellationToken ct = default);

    /// <summary>Marks the feed as seen up to now, which is what makes "new" mean anything.</summary>
    Task MarkFeedSeenAsync(Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class FollowService(IAppDbContext db, INotificationQueue notifications) : IFollowService
{
    private const int MaxLimit = 100;

    public async Task<FollowStateResponse> FollowPlaylistAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        var owner = await db.Playlists
            .Where(p => p.Id == playlistId && p.Visibility != PlaylistVisibility.Private)
            .Select(p => (Guid?)p.OwnerId)
            .FirstOrDefaultAsync(ct)
            // Private playlists are indistinguishable from missing ones, here as everywhere.
            ?? throw new NotFoundException("Playlist not found.");

        if (owner == userId)
        {
            throw new ValidationException("playlistId", "You already see everything in your own playlists.");
        }

        if (!await db.Follows.AnyAsync(f => f.FollowerId == userId && f.PlaylistId == playlistId, ct))
        {
            db.Follows.Add(new Follow { FollowerId = userId, PlaylistId = playlistId });
            await db.SaveChangesAsync(ct);

            // Inside the branch, so following, unfollowing and following again does not mail
            // somebody three times about the same person.
            notifications.QueueFollow(owner, playlistId, userId);
        }

        return new FollowStateResponse(true, await FollowerCountAsync(playlistId, null, ct));
    }

    public async Task<FollowStateResponse> UnfollowPlaylistAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        var follow = await db.Follows
            .FirstOrDefaultAsync(f => f.FollowerId == userId && f.PlaylistId == playlistId, ct);

        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync(ct);
        }

        return new FollowStateResponse(false, await FollowerCountAsync(playlistId, null, ct));
    }

    public async Task<FollowStateResponse> FollowUserAsync(
        Guid userId, string username, CancellationToken ct = default)
    {
        var targetId = await ResolveUserAsync(username, ct);

        if (targetId == userId)
        {
            throw new ValidationException("username", "You cannot follow yourself.");
        }

        if (!await db.Follows.AnyAsync(f => f.FollowerId == userId && f.FollowedUserId == targetId, ct))
        {
            db.Follows.Add(new Follow { FollowerId = userId, FollowedUserId = targetId });
            await db.SaveChangesAsync(ct);
        }

        return new FollowStateResponse(true, await FollowerCountAsync(null, targetId, ct));
    }

    public async Task<FollowStateResponse> UnfollowUserAsync(
        Guid userId, string username, CancellationToken ct = default)
    {
        var targetId = await ResolveUserAsync(username, ct);

        var follow = await db.Follows
            .FirstOrDefaultAsync(f => f.FollowerId == userId && f.FollowedUserId == targetId, ct);

        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync(ct);
        }

        return new FollowStateResponse(false, await FollowerCountAsync(null, targetId, ct));
    }

    public async Task<FollowingResponse> ListFollowingAsync(Guid userId, CancellationToken ct = default)
    {
        var playlists = await db.Follows
            .Where(f => f.FollowerId == userId && f.PlaylistId != null)
            .Select(f => new FollowedPlaylist(
                f.PlaylistId!.Value,
                f.Playlist!.Name,
                f.Playlist.Slug,
                db.Users.Where(u => u.Id == f.Playlist.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                f.Playlist.Items.Count(i => i.Link!.EnrichedAt != null),
                f.CreationTime))
            .ToListAsync(ct);

        // Sorted here rather than in SQL: EF cannot order on a type the query has just
        // constructed, and a person's follow list is small enough that it makes no difference.
        playlists = [.. playlists.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

        var users = await db.Follows
            .Where(f => f.FollowerId == userId && f.FollowedUserId != null)
            .Select(f => new FollowedUser(
                db.Users.Where(u => u.Id == f.FollowedUserId).Select(u => u.UserName!).FirstOrDefault()!,
                db.Playlists.Count(p =>
                    p.OwnerId == f.FollowedUserId && p.Visibility == PlaylistVisibility.Public),
                f.CreationTime))
            .ToListAsync(ct);

        return new FollowingResponse(
            playlists,
            [.. users.OrderBy(u => u.Username, StringComparer.CurrentCultureIgnoreCase)]);
    }

    public async Task<FeedResponse> GetFeedAsync(
        Guid userId, int? limit, string? cursor, CancellationToken ct = default)
    {
        var take = Paging.Take(limit, MaxLimit, fallback: 25);
        var seenAt = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FeedSeenAt)
            .FirstOrDefaultAsync(ct);

        var items = FeedQuery(userId);

        // Counted before paging, and against the caller's own last look rather than a fixed
        // window — "new since you looked" is the only version of new that means anything.
        var newCount = seenAt is null
            ? await items.CountAsync(ct)
            : await items.Where(i => i.CreationTime > seenAt).CountAsync(ct);

        var after = Cursor.DecodeTimeKey(cursor);

        var rows = await items
            // Keyset, not an offset: this is the feed, everybody followed keeps adding to it, and
            // an offset page shifts under the reader every time one of them does.
            .Where(i => after == null
                || i.CreationTime < after.Value.At
                || (i.CreationTime == after.Value.At && i.Id.CompareTo(after.Value.Id) < 0))
            .OrderByDescending(i => i.CreationTime)
            .ThenByDescending(i => i.Id)
            .Take(take + 1)
            .Select(i => new KeyedRow<FeedItem>(
                i.CreationTime,
                i.Id,
                new FeedItem(
                i.Id,
                i.LinkId,
                i.PlaylistId,
                i.Playlist!.Name,
                i.Playlist.Slug,
                db.Users.Where(u => u.Id == i.Playlist.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                i.Link!.CanonicalUrl,
                i.Link.Title,
                i.Link.Host!.Hostname,
                i.Link.Kind,
                i.Link.WordCount,
                i.CreationTime)))
            .ToListAsync(ct);

        var page = rows.ToPage(take);
        return new FeedResponse(page.Items, page.NextCursor, newCount, seenAt);
    }

    public async Task MarkFeedSeenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        user.FeedSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Everything in anything the caller follows — playlists directly, and the public playlists
    /// of people they follow, including ones made after the follow.
    /// </summary>
    private IQueryable<PlaylistItem> FeedQuery(Guid userId) =>
        db.PlaylistItems.Where(i =>
            i.Link!.EnrichedAt != null
            && i.Playlist!.Visibility != PlaylistVisibility.Private
            && (db.Follows.Any(f => f.FollowerId == userId && f.PlaylistId == i.PlaylistId)
                || db.Follows.Any(f => f.FollowerId == userId && f.FollowedUserId == i.Playlist.OwnerId)));

    private async Task<Guid> ResolveUserAsync(string username, CancellationToken ct)
    {
        var normalized = username.Trim().ToUpperInvariant();

        return await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("User not found.");
    }

    private Task<int> FollowerCountAsync(Guid? playlistId, Guid? userId, CancellationToken ct) =>
        playlistId is { } playlist
            ? db.Follows.CountAsync(f => f.PlaylistId == playlist, ct)
            : db.Follows.CountAsync(f => f.FollowedUserId == userId, ct);
}
