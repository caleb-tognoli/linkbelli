using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Who may do what in a playlist. One place, because the alternative is the same ownership test
/// written out at every call site and each one deciding for itself what a collaborator can do.
/// </summary>
public interface IPlaylistAccess
{
    /// <summary>
    /// What this person may do here, or null if they may do nothing. Owners come back as
    /// <see cref="PlaylistRole.Editor"/> — the extra things only an owner can do are asked about
    /// separately, because they are about the playlist rather than about editing it.
    /// </summary>
    Task<PlaylistRole?> RoleAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    /// <summary>
    /// Throws unless this person has at least this role. Refusals are 404, not 403: a playlist
    /// someone may not touch should not be confirmed to exist.
    /// </summary>
    Task EnsureAsync(Guid userId, Guid playlistId, PlaylistRole atLeast, CancellationToken ct = default);

    /// <summary>Throws unless this person owns it. Sharing settings and deletion stay with them.</summary>
    Task EnsureOwnerAsync(Guid userId, Guid playlistId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PlaylistAccess(IAppDbContext db) : IPlaylistAccess
{
    public async Task<PlaylistRole?> RoleAsync(Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        var owned = await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == userId, ct);
        if (owned)
        {
            return PlaylistRole.Editor;
        }

        var member = await db.PlaylistMembers
            .Where(m => m.PlaylistId == playlistId && m.UserId == userId)
            .Select(m => (PlaylistRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        return member;
    }

    public async Task EnsureAsync(
        Guid userId, Guid playlistId, PlaylistRole atLeast, CancellationToken ct = default)
    {
        var role = await RoleAsync(userId, playlistId, ct);

        if (role is null || role < atLeast)
        {
            // The same answer whether it does not exist, is not theirs, or is theirs to read but
            // not to change — none of which they are entitled to be able to tell apart.
            throw new NotFoundException("Playlist not found.");
        }
    }

    public async Task EnsureOwnerAsync(Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        if (!await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == userId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }
    }
}
