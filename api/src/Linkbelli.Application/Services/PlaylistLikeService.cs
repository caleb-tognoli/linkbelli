using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>Liking a playlist, and taking it back.</summary>
public interface IPlaylistLikeService
{
    /// <summary>
    /// Likes a playlist the caller can see. Idempotent: liking twice is one like, because a
    /// double tap should not be a way to inflate a number.
    /// </summary>
    Task<PlaylistLikeResponse> LikeAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Takes the like back. Also idempotent.</summary>
    Task<PlaylistLikeResponse> UnlikeAsync(Guid userId, Guid playlistId, CancellationToken ct = default);
}

/// <inheritdoc />
public class PlaylistLikeService(IAppDbContext db) : IPlaylistLikeService
{
    public async Task<PlaylistLikeResponse> LikeAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        await EnsureVisibleAsync(userId, playlistId, ct);

        // Idempotent: a double tap is one like, not two.
        if (!await db.PlaylistLikes.AnyAsync(l => l.PlaylistId == playlistId && l.UserId == userId, ct))
        {
            db.PlaylistLikes.Add(new PlaylistLike { PlaylistId = playlistId, UserId = userId });
            await db.SaveChangesAsync(ct);
        }

        return await LikeStateAsync(userId, playlistId, ct);
    }

    public async Task<PlaylistLikeResponse> UnlikeAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        await EnsureVisibleAsync(userId, playlistId, ct);

        var like = await db.PlaylistLikes
            .FirstOrDefaultAsync(l => l.PlaylistId == playlistId && l.UserId == userId, ct);

        if (like is not null)
        {
            db.PlaylistLikes.Remove(like);
            await db.SaveChangesAsync(ct);
        }

        return await LikeStateAsync(userId, playlistId, ct);
    }

    /// <summary>
    /// A playlist has to be visible to be liked — you cannot vote on something you were never
    /// shown. Private playlists you own are allowed, which costs nothing and saves a special case.
    /// </summary>
    private async Task EnsureVisibleAsync(Guid userId, Guid playlistId, CancellationToken ct)
    {
        var visible = await db.Playlists.AnyAsync(
            p => p.Id == playlistId
                && (p.Visibility != PlaylistVisibility.Private || p.OwnerId == userId),
            ct);

        if (!visible)
        {
            throw new NotFoundException("Playlist not found.");
        }
    }

    private async Task<PlaylistLikeResponse> LikeStateAsync(Guid userId, Guid playlistId, CancellationToken ct) =>
        new(playlistId,
            await db.PlaylistLikes.CountAsync(l => l.PlaylistId == playlistId, ct),
            await db.PlaylistLikes.AnyAsync(l => l.PlaylistId == playlistId && l.UserId == userId, ct));
}
