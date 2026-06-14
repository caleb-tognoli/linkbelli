using System.Text;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Mapping;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class PlaylistService(IAppDbContext db) : IPlaylistService
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
        Guid ownerId, int? limit, string? cursor, string[]? tags, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? Math.Max(0, o) : 0;

        var query = FilterByTags(db.Playlists.Where(p => p.OwnerId == ownerId), tags);

        // "Recently updated" = most recent of the playlist's own creation and its newest item.
        // (Items are always created after their playlist, so coalesce == greatest.)
        var rows = await query
            .Select(p => new
            {
                Playlist = p,
                LastActivity = p.Items.Max(i => (DateTimeOffset?)i.CreationTime) ?? p.CreationTime,
                ItemCount = p.Items.Count(),
                Tags = p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
            })
            .OrderByDescending(x => x.LastActivity).ThenByDescending(x => x.Playlist.Id)
            .Skip(offset).Take(take + 1)
            .Select(x => new PlaylistResponse(
                x.Playlist.Id, x.Playlist.Name, x.Playlist.Slug, x.Playlist.Description,
                x.Playlist.Visibility, x.ItemCount, x.Playlist.CreationTime, x.Tags))
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

        var tags = await ResolveTagsAsync(TagNormalizer.Normalize(request.Tags), ct);

        var playlist = new Playlist
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(ownerId, request.Name, ct),
            Description = request.Description?.Trim(),
            Visibility = request.Visibility ?? PlaylistVisibility.Private,
        };
        db.Playlists.Add(playlist);
        foreach (var t in tags)
        {
            db.PlaylistTags.Add(new PlaylistTag { PlaylistId = playlist.Id, TagId = t.Id });
        }

        await db.SaveChangesAsync(ct);

        return playlist.ToResponse(0, tags.Select(t => t.Name));
    }

    public async Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists
            .Where(p => p.Id == id && p.OwnerId == ownerId)
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility, p.Items.Count(), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray()))
            .FirstOrDefaultAsync(ct);

        return playlist ?? throw new NotFoundException("Playlist not found.");
    }

    public async Task<PlaylistResponse> UpdateAsync(Guid ownerId, Guid id, UpdatePlaylistRequest request, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct)
                       ?? throw new NotFoundException("Playlist not found.");

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

        if (request.Tags is not null)
        {
            var tags = await ResolveTagsAsync(TagNormalizer.Normalize(request.Tags), ct);
            var existing = await db.PlaylistTags.Where(pt => pt.PlaylistId == id).ToListAsync(ct);
            db.PlaylistTags.RemoveRange(existing);
            foreach (var t in tags)
            {
                db.PlaylistTags.Add(new PlaylistTag { PlaylistId = id, TagId = t.Id });
            }
        }

        await db.SaveChangesAsync(ct);

        var count = await db.PlaylistItems.CountAsync(i => i.PlaylistId == id, ct);
        var tagNames = await db.PlaylistTags.Where(pt => pt.PlaylistId == id).Select(pt => pt.Tag!.Name).ToArrayAsync(ct);
        return playlist.ToResponse(count, tagNames);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, ct)
                       ?? throw new NotFoundException("Playlist not found.");

        db.Playlists.Remove(playlist); // soft delete
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlaylistResponse> GetPublicAsync(string username, string slug, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();
        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility, p.Items.Count(), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray()))
            .FirstOrDefaultAsync(ct);

        // Private (or missing) playlists are indistinguishable to anonymous callers.
        return playlist ?? throw new NotFoundException("Playlist not found.");
    }

    public async Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(
        string? q, string[]? tags, int? limit, string? cursor, CancellationToken ct = default)
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

        var rows = await (from p in query
                          join u in db.Users on p.OwnerId equals u.Id
                          orderby p.CreationTime descending, p.Id descending
                          select new PublicPlaylistSummary(
                              u.UserName!, p.Slug, p.Name, p.Description,
                              p.Items.Count(), p.CreationTime,
                              p.Tags.Select(pt => pt.Tag!.Name).ToArray()))
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

    public async Task SubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default)
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
        if (alreadyAttached)
        {
            return; // idempotent
        }

        db.PlaylistSources.Add(new PlaylistSource { PlaylistId = playlistId, SourceId = sourceId });
        await db.SaveChangesAsync(ct);
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

    /// <summary>Get-or-create tags by normalized name (race-safe, like Host/Link).</summary>
    private async Task<List<Tag>> ResolveTagsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var resolved = await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync(ct);
        var missing = names.Where(n => resolved.All(t => t.Name != n)).Select(n => new Tag { Name = n }).ToList();
        if (missing.Count == 0)
        {
            return resolved;
        }

        db.Tags.AddRange(missing);
        try
        {
            await db.SaveChangesAsync(ct);
            resolved.AddRange(missing);
            return resolved;
        }
        catch (DbUpdateException)
        {
            // Lost a race creating one or more tags — re-read the authoritative rows.
            foreach (var t in missing)
            {
                db.Entry(t).State = EntityState.Detached;
            }

            return await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync(ct);
        }
    }

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
