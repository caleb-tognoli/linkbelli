using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Who else is in a playlist. Sharing was all-or-nothing public: showing one list to one person
/// meant publishing it to everyone, and collaborating on one meant handing over an account.
/// </summary>
public interface IPlaylistMemberService
{
    Task<IReadOnlyList<PlaylistMemberResponse>> ListAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Adds someone, or changes what they may do. The owner's call either way.</summary>
    Task<PlaylistMemberResponse> SetAsync(
        Guid ownerId, Guid playlistId, string username, PlaylistRole role, CancellationToken ct = default);

    /// <summary>
    /// Removes someone. Also how a member leaves: they can always remove themselves, which
    /// otherwise requires asking the owner to let them go.
    /// </summary>
    Task RemoveAsync(Guid userId, Guid playlistId, string username, CancellationToken ct = default);

    /// <summary>Playlists other people have shared with the caller.</summary>
    Task<IReadOnlyList<SharedPlaylistResponse>> ListSharedWithMeAsync(
        Guid userId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PlaylistMemberService(IAppDbContext db, IPlaylistAccess access) : IPlaylistMemberService
{
    /// <summary>
    /// People one playlist can be shared with. A ceiling rather than a policy: past this it is a
    /// public playlist, and should be one.
    /// </summary>
    public const int MaxMembers = 50;

    public async Task<IReadOnlyList<PlaylistMemberResponse>> ListAsync(
        Guid userId, Guid playlistId, CancellationToken ct = default)
    {
        // Anyone in the playlist can see who else is: being in a shared list without knowing who
        // else can read it is worse than not sharing.
        await access.EnsureAsync(userId, playlistId, PlaylistRole.Viewer, ct);

        return await db.PlaylistMembers
            .Where(m => m.PlaylistId == playlistId)
            .Select(m => new PlaylistMemberResponse(
                db.Users.Where(u => u.Id == m.UserId).Select(u => u.UserName!).FirstOrDefault()!,
                m.Role,
                m.CreationTime))
            .ToListAsync(ct);
    }

    public async Task<PlaylistMemberResponse> SetAsync(
        Guid ownerId, Guid playlistId, string username, PlaylistRole role, CancellationToken ct = default)
    {
        await access.EnsureOwnerAsync(ownerId, playlistId, ct);

        var (userId, resolvedName) = await ResolveAsync(username, ct);

        if (userId == ownerId)
        {
            throw new ValidationException("username", "You already own this playlist.");
        }

        var member = await db.PlaylistMembers
            .FirstOrDefaultAsync(m => m.PlaylistId == playlistId && m.UserId == userId, ct);

        if (member is null)
        {
            if (await db.PlaylistMembers.CountAsync(m => m.PlaylistId == playlistId, ct) >= MaxMembers)
            {
                throw new ValidationException(
                    "members", $"A playlist can be shared with at most {MaxMembers} people.");
            }

            member = new PlaylistMember
            {
                PlaylistId = playlistId,
                UserId = userId,
                InvitedBy = ownerId,
            };
            db.PlaylistMembers.Add(member);
        }

        member.Role = role;
        await db.SaveChangesAsync(ct);

        return new PlaylistMemberResponse(resolvedName, member.Role, member.CreationTime);
    }

    public async Task RemoveAsync(
        Guid userId, Guid playlistId, string username, CancellationToken ct = default)
    {
        var (targetId, _) = await ResolveAsync(username, ct);

        // Either the owner is removing someone, or someone is letting themselves out. Leaving
        // should never require asking the person you are leaving.
        if (targetId != userId)
        {
            await access.EnsureOwnerAsync(userId, playlistId, ct);
        }

        var member = await db.PlaylistMembers
            .FirstOrDefaultAsync(m => m.PlaylistId == playlistId && m.UserId == targetId, ct);

        if (member is null)
        {
            return;
        }

        db.PlaylistMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SharedPlaylistResponse>> ListSharedWithMeAsync(
        Guid userId, CancellationToken ct = default) =>
        await db.PlaylistMembers
            .Where(m => m.UserId == userId)
            .Select(m => new SharedPlaylistResponse(
                m.PlaylistId,
                m.Playlist!.Name,
                db.Users.Where(u => u.Id == m.Playlist.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                m.Role,
                m.Playlist.Items.Count(i => i.Link!.EnrichedAt != null),
                m.CreationTime))
            .ToListAsync(ct);

    private async Task<(Guid Id, string Username)> ResolveAsync(string username, CancellationToken ct)
    {
        var normalized = username.Trim().ToUpperInvariant();

        var user = await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => new { u.Id, Name = u.UserName! })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("User not found.");

        return (user.Id, user.Name);
    }
}
