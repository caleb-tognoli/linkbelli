using System.Text;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Mapping;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class PlaylistService(
    IAppDbContext db,
    IUserPreferenceService prefs,
    ITagResolver tags,
    IPlaylistAccess access,
    IAuditLog audit) : IPlaylistService
{
    private const int MaxTagResults = 200;

    /// <summary>Applies an AND tag filter: the playlist must carry every (normalized) tag.</summary>
    private static IQueryable<Playlist> FilterByTags(IQueryable<Playlist> query, string[]? tags)
    {
        foreach (var raw in tags ?? [])
        {
            var name = TagNormalizer.NormalizeOne(raw);
            if (name.Length > 0)
            {
                query = query.Where(p => p.Tags.Any(pt => pt.Tag!.Name == name));
            }
        }

        return query;
    }

    public async Task<PagedResult<PlaylistResponse>> ListAsync(
        Guid ownerId, int? limit, string? cursor, string[]? tags, string? q = null, bool unfiled = false, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? Math.Max(0, o) : 0;

        var query = FilterByTags(db.Playlists.Where(p => p.OwnerId == ownerId), tags);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(needle));
        }

        // The home "root" view shows only playlists not filed in any of the caller's folders.
        if (unfiled)
        {
            query = query.Where(p => !db.FolderPlaylists.Any(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id));
        }

        query = query.VisibleTo(await prefs.ShowNsfwAsync(ownerId, ct));

        // "Recently updated" = most recent of the playlist's own creation and its newest item.
        // (Items are always created after their playlist, so coalesce == greatest.)
        // ItemCount counts only enriched items (the only ones the UI shows).
        var rows = await query
            .Select(p => new
            {
                Playlist = p,
                LastActivity = p.Items.Max(i => (DateTimeOffset?)i.CreationTime) ?? p.CreationTime,
                ItemCount = p.Items.Count(i => i.Link!.EnrichedAt != null),
                PendingCount = p.Items.Count(i => i.Link!.EnrichedAt == null),
                Tags = p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                Nsfw = p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                FolderId = db.FolderPlaylists
                    .Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id)
                    .Select(fp => (Guid?)fp.FolderId).FirstOrDefault(),
                FolderName = db.FolderPlaylists
                    .Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id)
                    .Select(fp => fp.Folder!.Name).FirstOrDefault(),
            })
            .OrderByDescending(x => x.LastActivity).ThenByDescending(x => x.Playlist.Id)
            .Skip(offset).Take(take + 1)
            .Select(x => new PlaylistResponse(
                x.Playlist.Id, x.Playlist.Name, x.Playlist.Slug, x.Playlist.Description,
                x.Playlist.Visibility, x.ItemCount, x.Playlist.CreationTime, x.Tags, x.Nsfw,
                x.FolderId, x.FolderName, null, x.PendingCount,
                AverageScore: null,
                ScoredCount: null,
                View: null,
                LikeCount: 0,
                LikedByMe: false,
                FollowerCount: 0,
                FollowedByMe: false,
                IsOwner: true,
                Role: null,
                CoverLinkId: x.Playlist.CoverLinkId))
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.Encode((offset + take).ToString());
        }

        return new PagedResult<PlaylistResponse>(rows, next);
    }

    public async Task<PlaylistResponse> CreateAsync(Guid ownerId, CreatePlaylistRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "Name is required.");
        }

        var resolvedTags = await tags.ResolveAsync(TagNormalizer.Normalize(request.Tags), ct);

        var playlist = new Playlist
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(ownerId, request.Name, ct),
            Description = request.Description?.Trim(),
            Visibility = request.Visibility ?? PlaylistVisibility.Private,
        };
        db.Playlists.Add(playlist);
        foreach (var t in resolvedTags)
        {
            db.PlaylistTags.Add(new PlaylistTag { PlaylistId = playlist.Id, TagId = t.Id });
        }

        await SaveWithUniqueSlugAsync(playlist, ownerId, ct);

        return playlist.ToResponse(0, resolvedTags.Select(t => t.Name), nsfw: false);
    }

    public async Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        // A playlist shared with someone is theirs to open — that is what sharing it means.
        var playlist = await db.Playlists
            .Where(p => p.Id == id
                && (p.OwnerId == ownerId
                    || db.PlaylistMembers.Any(m => m.PlaylistId == p.Id && m.UserId == ownerId)))
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility,
                p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                db.FolderPlaylists.Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id)
                    .Select(fp => (Guid?)fp.FolderId).FirstOrDefault(),
                db.FolderPlaylists.Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id)
                    .Select(fp => fp.Folder!.Name).FirstOrDefault(),
                // The owner's own read reports whether they set the flag by hand, so the control
                // can show its real state rather than guessing.
                p.NsfwOverride == null ? NsfwSetting.Auto : p.NsfwOverride.Value ? NsfwSetting.Yes : NsfwSetting.No,
                p.Items.Count(i => i.Link!.EnrichedAt == null),
                // Averaged over the items that were actually rated — counting unrated ones as
                // zero would drag the number down and say something false about the playlist.
                p.Items.Where(i => i.Score != null).Average(i => (double?)i.Score),
                p.Items.Count(i => i.Score != null),
                db.PlaylistPreferences
                    .Where(pp => pp.OwnerId == ownerId && pp.PlaylistId == p.Id)
                    .Select(pp => new PlaylistViewPreferences(
                        pp.Sort, pp.Source, pp.Status, pp.ShowUrls, pp.ShowThumbnails, pp.ViewMode))
                    .FirstOrDefault(),
                LikeCount: 0,
                LikedByMe: false,
                FollowerCount: 0,
                FollowedByMe: false,
                // What the caller may do here, so the page can show the controls that will
                // actually work rather than ones that 404 when pressed.
                p.OwnerId == ownerId,
                db.PlaylistMembers
                    .Where(m => m.PlaylistId == p.Id && m.UserId == ownerId)
                    .Select(m => (PlaylistRole?)m.Role)
                    .FirstOrDefault(),
                p.CoverLinkId))
            .FirstOrDefaultAsync(ct);

        return playlist ?? throw new NotFoundException("Playlist not found.");
    }

    public async Task<PlaylistResponse> UpdateAsync(Guid ownerId, Guid id, UpdatePlaylistRequest request, CancellationToken ct = default)
    {
        // Editors can rename and re-tag; who it is shared with, and whether it is public, stays
        // with the owner and is checked again below.
        await access.EnsureAsync(ownerId, id, PlaylistRole.Editor, ct);

        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == id, ct)
                       ?? throw new NotFoundException("Playlist not found.");

        if (request.Visibility is not null && playlist.OwnerId != ownerId)
        {
            throw new ValidationException(
                "visibility", "Only the owner can change who a playlist is shared with.");
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException("name", "Name cannot be empty.");
            }

            playlist.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            playlist.Description = request.Description.Trim();
        }

        if (request.Visibility is not null)
        {
            playlist.Visibility = request.Visibility.Value;
        }

        if (request.Nsfw is not null)
        {
            // Three states on the wire: "yes", "no", and "auto" — which hands the decision back
            // to the items. Detection is a self-declared meta tag, so the owner gets the last word.
            playlist.NsfwOverride = request.Nsfw.Value switch
            {
                NsfwSetting.Yes => true,
                NsfwSetting.No => false,
                _ => null,
            };
        }

        if (request.Tags is not null)
        {
            var resolvedTags = await tags.ResolveAsync(TagNormalizer.Normalize(request.Tags), ct);
            var existing = await db.PlaylistTags.Where(pt => pt.PlaylistId == id).ToListAsync(ct);
            db.PlaylistTags.RemoveRange(existing);
            foreach (var t in resolvedTags)
            {
                db.PlaylistTags.Add(new PlaylistTag { PlaylistId = id, TagId = t.Id });
            }

            // Same as item tags: the join rows change, the playlist row does not, and a client
            // syncing on LastModified would never hear about it.
            playlist.LastModified = DateTimeOffset.UtcNow;
        }

        if (request.CoverLinkId is { } cover)
        {
            // Guid.Empty clears it: null already means "leave it alone" everywhere else on this
            // request, so there has to be some way to say "no cover".
            if (cover == Guid.Empty)
            {
                playlist.CoverLinkId = null;
            }
            else
            {
                // One of its own items, not any link that happens to exist — a cover is chosen
                // from what is in the playlist, and this is also what stops it pointing at
                // somebody else's picture.
                var inPlaylist = await db.PlaylistItems.AnyAsync(
                    i => i.PlaylistId == id && i.LinkId == cover, ct);

                if (!inPlaylist)
                {
                    throw new ValidationException("coverLinkId", "That link isn't in this playlist.");
                }

                playlist.CoverLinkId = cover;
            }
        }

        await db.SaveChangesAsync(ct);

        var count = await db.PlaylistItems.CountAsync(i => i.PlaylistId == id && i.Link!.EnrichedAt != null, ct);
        var nsfw = playlist.NsfwOverride
            ?? await db.PlaylistItems.AnyAsync(i => i.PlaylistId == id && i.Link!.Nsfw, ct);
        var tagNames = await db.PlaylistTags.Where(pt => pt.PlaylistId == id).Select(pt => pt.Tag!.Name).ToArrayAsync(ct);
        var folder = await db.FolderPlaylists
            .Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == id)
            .Select(fp => new { fp.FolderId, fp.Folder!.Name })
            .FirstOrDefaultAsync(ct);
        return playlist.ToResponse(count, tagNames, nsfw, folder?.FolderId, folder?.Name);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct)
                       ?? throw new NotFoundException("Playlist not found.");

        var itemCount = await db.PlaylistItems.CountAsync(i => i.PlaylistId == id, ct);

        db.Playlists.Remove(playlist); // soft delete
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            ownerId, "playlist.delete", "playlist", id,
            $"Deleted \"{playlist.Name}\" and its {itemCount} items.",
            new { playlist.Name, playlist.Slug, playlist.Visibility, itemCount },
            ct: ct);
    }

    public async Task<PlaylistResponse> GetPublicAsync(string username, string slug, Guid? viewerId, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility,
                p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                // The viewer's own private folder placement (if they saved this playlist); null when anonymous.
                db.FolderPlaylists.Where(fp => fp.OwnerId == viewerId && fp.PlaylistId == p.Id)
                    .Select(fp => (Guid?)fp.FolderId).FirstOrDefault(),
                db.FolderPlaylists.Where(fp => fp.OwnerId == viewerId && fp.PlaylistId == p.Id)
                    .Select(fp => fp.Folder!.Name).FirstOrDefault(),
                NsfwSetting: null,
                PendingCount: null,
                AverageScore: null,
                ScoredCount: null,
                View: null,
                db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                db.PlaylistLikes.Any(l => l.PlaylistId == p.Id && l.UserId == viewerId),
                db.Follows.Count(f => f.PlaylistId == p.Id),
                db.Follows.Any(f => f.PlaylistId == p.Id && f.FollowerId == viewerId),
                IsOwner: false,
                Role: null,
                p.CoverLinkId))
            .FirstOrDefaultAsync(ct);

        // Private/missing — and NSFW for viewers who haven't opted in — are all indistinguishable.
        if (playlist is null || (playlist.Nsfw && !await prefs.ShowNsfwAsync(viewerId, ct)))
        {
            throw new NotFoundException("Playlist not found.");
        }

        return playlist;
    }

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

    public async Task SaveViewAsync(
        Guid ownerId, Guid playlistId, PlaylistViewPreferences view, CancellationToken ct = default)
    {
        if (!await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }

        var preference = await db.PlaylistPreferences
            .FirstOrDefaultAsync(pp => pp.OwnerId == ownerId && pp.PlaylistId == playlistId, ct);

        if (preference is null)
        {
            preference = new PlaylistPreference { OwnerId = ownerId, PlaylistId = playlistId };
            db.PlaylistPreferences.Add(preference);
        }

        preference.Sort = Trim(view.Sort, 32);
        preference.Source = Trim(view.Source, 64);
        preference.Status = Trim(view.Status, 16);
        preference.ShowUrls = view.ShowUrls;
        preference.ShowThumbnails = view.ShowThumbnails;
        preference.ViewMode = Trim(view.ViewMode, 16);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Keeps a client-supplied value inside the column it has to fit in.</summary>
    private static string? Trim(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
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

        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? Math.Max(0, o) : 0;

        // Public only: Unlisted is share-by-link, so it never appears in a listing, not even the
        // owner's own profile page.
        var query = db.Playlists
            .Where(p => p.OwnerId == ownerId && p.Visibility == PlaylistVisibility.Public)
            .VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        var rows = await (from p in query
                          join u in db.Users on p.OwnerId equals u.Id
                          orderby p.CreationTime descending, p.Id descending
                          select new PublicPlaylistSummary(
                              u.UserName!, p.Slug, p.Name, p.Description,
                              p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                              p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                              p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                              db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                              p.Items.Max(i => (DateTimeOffset?)i.CreationTime)))
            .Skip(offset).Take(take + 1)
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.Encode((offset + take).ToString());
        }

        return new PagedResult<PublicPlaylistSummary>(rows, next);
    }

    public async Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(
        string? q, string[]? tags, string? sort, int? limit, string? cursor, Guid? viewerId,
        CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? Math.Max(0, o) : 0;

        var query = db.Playlists.Where(p => p.Visibility == PlaylistVisibility.Public);
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Provider-agnostic case-insensitive contains (translates to LOWER(name) LIKE …).
            var needle = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(needle));
        }

        query = FilterByTags(query, tags);

        query = query.VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        // Ordered first, projected second: the owner's name comes from a subquery rather than a
        // join so the ordering stays expressed over the playlist itself, which is the only form
        // EF can translate.
        var rows = await Rank(query, sort)
            .Select(p => new PublicPlaylistSummary(
                db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                p.Slug, p.Name, p.Description,
                p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                p.Items.Max(i => (DateTimeOffset?)i.CreationTime)))
            .Skip(offset).Take(take + 1)
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.Encode((offset + take).ToString());
        }

        return new PagedResult<PublicPlaylistSummary>(rows, next);
    }

    /// <summary>
    /// How discovery orders what it found.
    /// </summary>
    /// <remarks>
    /// Ordering everything by age rewards being new rather than being good, and a list posted
    /// last year that people keep coming back to was unfindable. Every ordering falls back to the
    /// creation date, so the page doesn't reshuffle between refreshes on a tie.
    ///
    /// Expressed over the entities rather than over the projected summary: EF cannot translate an
    /// OrderBy that reaches into a type the query has just constructed.
    /// </remarks>
    private IOrderedQueryable<Playlist> Rank(IQueryable<Playlist> playlists, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            // A list nobody has added to in a year is finished, whatever else it is.
            "active" => playlists
                .OrderByDescending(p =>
                    p.Items.Max(i => (DateTimeOffset?)i.CreationTime) ?? p.CreationTime)
                .ThenByDescending(p => p.CreationTime),

            "liked" => playlists
                .OrderByDescending(p => db.PlaylistLikes.Count(l => l.PlaylistId == p.Id))
                .ThenByDescending(p => p.CreationTime),

            "largest" => playlists
                .OrderByDescending(p => p.Items.Count(i => i.Link!.EnrichedAt != null))
                .ThenByDescending(p => p.CreationTime),

            // Newest first: what discovery has always done, and still the right default for a
            // page whose job is to show you something you haven't seen.
            _ => playlists
                .OrderByDescending(p => p.CreationTime)
                .ThenByDescending(p => p.Id),
        };

    /// <summary>
    /// Public playlists that look like this one: sharing its tags, or holding the same links.
    /// </summary>
    /// <remarks>
    /// Shared links are the stronger signal and are weighted accordingly — two lists holding the
    /// same twenty pages are about the same thing whatever anyone tagged them.
    /// </remarks>
    public async Task<IReadOnlyList<PublicPlaylistSummary>> ListSimilarAsync(
        string username, string slug, int? limit, Guid? viewerId, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 6, 1, 24);
        var normalized = username.ToUpperInvariant();

        var subject = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new
            {
                p.Id,
                Tags = p.Tags.Select(pt => pt.TagId).ToList(),
                Links = p.Items.Select(i => i.LinkId).ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Playlist not found.");

        // Nothing to be similar to. An empty row is better than a row of arbitrary playlists
        // dressed up as recommendations.
        if (subject.Tags.Count == 0 && subject.Links.Count == 0)
        {
            return [];
        }

        var candidates = db.Playlists.Where(p =>
            p.Visibility == PlaylistVisibility.Public && p.Id != subject.Id);

        candidates = candidates.VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        return await (from p in candidates
                      join u in db.Users on p.OwnerId equals u.Id
                      let sharedTags = p.Tags.Count(pt => subject.Tags.Contains(pt.TagId))
                      let sharedLinks = p.Items.Count(i => subject.Links.Contains(i.LinkId))
                      where sharedTags > 0 || sharedLinks > 0
                      orderby sharedLinks * SharedLinkWeight + sharedTags descending,
                              p.CreationTime descending
                      select new PublicPlaylistSummary(
                          u.UserName!, p.Slug, p.Name, p.Description,
                          p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                          p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                          p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                          db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                          p.Items.Max(i => (DateTimeOffset?)i.CreationTime)))
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>How much more a shared link counts than a shared tag.</summary>
    private const int SharedLinkWeight = 3;

    /// <summary>
    /// The tags on public playlists that have seen activity lately, rather than the ones with the
    /// biggest all-time count — which is a list that never changes.
    /// </summary>
    public async Task<IReadOnlyList<TagSummary>> ListTrendingTagsAsync(
        int? days, CancellationToken ct = default)
    {
        var window = Math.Clamp(days ?? TrendingWindowDays, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-window);

        var rows = await db.PlaylistTags
            .Where(pt => pt.Playlist!.Visibility == PlaylistVisibility.Public
                && (pt.Playlist.CreationTime >= since
                    || pt.Playlist.Items.Any(i => i.CreationTime >= since)))
            .GroupBy(pt => pt.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(MaxTagResults)
            .ToListAsync(ct);

        return [.. rows.Select(r => new TagSummary(r.Name, r.Count))];
    }

    /// <summary>How far back "trending" looks when the caller doesn't say.</summary>
    public const int TrendingWindowDays = 30;

    public Task<IReadOnlyList<TagSummary>> ListOwnTagsAsync(Guid ownerId, string? q, CancellationToken ct = default) =>
        ListTagsAsync(db.PlaylistTags.Where(pt => pt.Playlist!.OwnerId == ownerId), q, ct);

    public Task<IReadOnlyList<TagSummary>> ListPublicTagsAsync(string? q, CancellationToken ct = default) =>
        ListTagsAsync(db.PlaylistTags.Where(pt => pt.Playlist!.Visibility == PlaylistVisibility.Public), q, ct);

    private static async Task<IReadOnlyList<TagSummary>> ListTagsAsync(
        IQueryable<PlaylistTag> scope, string? q, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var prefix = TagNormalizer.NormalizeOne(q);
            scope = scope.Where(pt => pt.Tag!.Name.StartsWith(prefix));
        }

        var rows = await scope
            .GroupBy(pt => pt.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(MaxTagResults)
            .ToListAsync(ct);

        return rows.Select(x => new TagSummary(x.Name, x.Count)).ToList();
    }

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

    /// <summary>How many times a slug collision is re-rolled before giving up.</summary>
    private const int SlugAttempts = 6;

    /// <summary>
    /// Saves a new playlist, settling a slug collision rather than failing on it.
    /// </summary>
    /// <remarks>
    /// Choosing the slug was a check-then-act: two requests naming a playlist the same thing both
    /// found the slug free, and the loser came back a 500. Which is not exotic — it is what
    /// double-clicking Create does, and what the offline queue does when it replays.
    ///
    /// The first retry re-scans, because that still produces the tidy "name-2" people expect. A
    /// second collision means several writers are re-scanning in lockstep and all picking the
    /// same next number, so from there it stops asking and takes a random discriminator: under
    /// contention the answer has to stop being a function of what everyone else can also see.
    /// </remarks>
    private async Task SaveWithUniqueSlugAsync(Playlist playlist, Guid ownerId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (UniqueConstraintException ex) when (ex.Involves("Slug") && attempt < SlugAttempts)
            {
                playlist.Slug = attempt == 1
                    ? await GenerateUniqueSlugAsync(ownerId, playlist.Name, ct)
                    : $"{Slugify(playlist.Name)}-{RandomDiscriminator()}";
            }
        }
    }

    /// <summary>
    /// A short suffix that two simultaneous writers will not agree on.
    /// </summary>
    /// <remarks>
    /// Four base-36 characters: short enough to still read as a slug, and 1.7 million values, so
    /// a second collision on top of the first is not something anyone will meet.
    /// </remarks>
    private static string RandomDiscriminator() =>
        Random.Shared.Next(36 * 36 * 36, 36 * 36 * 36 * 36).ToString("x").PadLeft(4, '0')[..4];

    /// <summary>
    /// The first free "name", "name-2", "name-3"… for this owner.
    /// </summary>
    /// <remarks>
    /// Advisory rather than authoritative: the unique index is what actually guarantees this, and
    /// <see cref="SaveWithUniqueSlugAsync"/> is what copes when two callers read the same answer.
    /// </remarks>
    private async Task<string> GenerateUniqueSlugAsync(Guid ownerId, string name, CancellationToken ct)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var n = 2;
        while (await db.Playlists.AnyAsync(p => p.OwnerId == ownerId && p.Slug == slug, ct))
        {
            slug = $"{baseSlug}-{n++}";
        }

        return slug;
    }

    private static string Slugify(string name)
    {
        var sb = new StringBuilder();
        var lastDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "playlist" : slug;
    }
}
