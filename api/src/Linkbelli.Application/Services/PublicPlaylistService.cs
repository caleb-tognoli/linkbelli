using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>A playlist and a profile as somebody without an account sees them.</summary>
public interface IPublicPlaylistService
{
    /// <summary>Anonymous read of a non-private playlist by owner username + slug. NSFW playlists 404 unless the viewer opted in.</summary>
    Task<PlaylistResponse> GetPublicAsync(string username, string slug, Guid? viewerId, CancellationToken ct = default);

    /// <summary>
    /// A user as seen from the outside. Throws NotFoundException for an unknown username — a
    /// profile that doesn't exist and one you can't see look the same.
    /// </summary>
    Task<PublicProfile> GetPublicProfileAsync(string username, Guid? viewerId, CancellationToken ct = default);

    /// <summary>One user's public playlists, newest first.</summary>
    Task<PagedResult<PublicPlaylistSummary>> ListUserPublicPlaylistsAsync(
        string username, int? limit, string? cursor, Guid? viewerId, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// Every read here is answerable to an anonymous caller, which is the reason it is its own class:
/// a change to what the owner sees should never be one edit away from changing what the whole
/// internet sees.
/// </remarks>
public class PublicPlaylistService(IAppDbContext db, IUserPreferenceService prefs) : IPublicPlaylistService
{
    public async Task<PlaylistResponse> GetPublicAsync(string username, string slug, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new PlaylistResponse
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                Description = p.Description,
                Visibility = p.Visibility,
                ItemCount = p.Items.Count(i => i.Link!.EnrichedAt != null),
                CreationTime = p.CreationTime,
                Tags = p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                Nsfw = p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                // The viewer's own private folder placement (if they saved this playlist); null when anonymous.
                FolderId = db.FolderPlaylists.Where(fp => fp.OwnerId == viewerId && fp.PlaylistId == p.Id)
                    .Select(fp => (Guid?)fp.FolderId).FirstOrDefault(),
                FolderName = db.FolderPlaylists.Where(fp => fp.OwnerId == viewerId && fp.PlaylistId == p.Id)
                    .Select(fp => fp.Folder!.Name).FirstOrDefault(),
                LikeCount = db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                LikedByMe = db.PlaylistLikes.Any(l => l.PlaylistId == p.Id && l.UserId == viewerId),
                FollowerCount = db.Follows.Count(f => f.PlaylistId == p.Id),
                FollowedByMe = db.Follows.Any(f => f.PlaylistId == p.Id && f.FollowerId == viewerId),
                // How many people kept a copy. Worth more than the like count on a page whose
                // whole job is deciding whether this list is for you.
                ForkCount = db.Playlists.Count(other => other.ForkedFromPlaylistId == p.Id),
                IsOwner = false,
                CoverLinkId = p.CoverLinkId,
            })
            .FirstOrDefaultAsync(ct);

        // Private/missing — and NSFW for viewers who haven't opted in — are all indistinguishable.
        if (playlist is null || (playlist.Nsfw && !await prefs.ShowNsfwAsync(viewerId, ct)))
        {
            throw new NotFoundException("Playlist not found.");
        }

        return playlist;
    }

    public async Task<PublicProfile> GetPublicProfileAsync(
        string username, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();

        var user = await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => new { u.Id, u.UserName, u.CreatedAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Profile not found.");

        var published = db.Playlists
            .Where(p => p.OwnerId == user.Id && p.Visibility == PlaylistVisibility.Public)
            .VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        // Counted in the database rather than by fetching one row per public playlist — each of
        // those rows was itself a correlated COUNT, for two numbers on an anonymous page.
        var playlistCount = await published.CountAsync(ct);
        var linkCount = await published.SumAsync(p => p.Items.Count(i => i.Link!.EnrichedAt != null), ct);

        return new PublicProfile(
            user.UserName!, user.CreatedAt, playlistCount, linkCount,
            await db.Follows.CountAsync(f => f.FollowedUserId == user.Id, ct),
            await db.Follows.AnyAsync(f => f.FollowedUserId == user.Id && f.FollowerId == viewerId, ct));
    }

    public async Task<PagedResult<PublicPlaylistSummary>> ListUserPublicPlaylistsAsync(
        string username, int? limit, string? cursor, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var ownerId = await db.Users
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Profile not found.");

        var take = Paging.Take(limit);
        var after = Cursor.DecodeTimeKey(cursor);

        // Public only: Unlisted is share-by-link, so it never appears in a listing, not even the
        // owner's own profile page.
        var query = db.Playlists
            .Where(p => p.OwnerId == ownerId && p.Visibility == PlaylistVisibility.Public)
            .VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        var rows = await (from p in query
                          join u in db.Users on p.OwnerId equals u.Id
                          where after == null
                              || p.CreationTime < after.Value.At
                              || (p.CreationTime == after.Value.At && p.Id.CompareTo(after.Value.Id) < 0)
                          orderby p.CreationTime descending, p.Id descending
                          select new KeyedRow<PublicPlaylistSummary>(
                              p.CreationTime,
                              p.Id,
                              new PublicPlaylistSummary(
                                  u.UserName!, p.Slug, p.Name, p.Description,
                                  p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                                  p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                                  p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                                  db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                                  p.Items.Max(i => (DateTimeOffset?)i.CreationTime))))
            .Take(take + 1)
            .ToListAsync(ct);

        return rows.ToPage(take);
    }
}
