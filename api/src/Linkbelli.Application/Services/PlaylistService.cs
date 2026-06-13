using System.Text;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Mapping;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class PlaylistService(IAppDbContext db) : IPlaylistService
{
    public async Task<PagedResult<PlaylistResponse>> ListAsync(
        Guid ownerId, int? limit, string? cursor, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var offset = Cursor.TryDecode(cursor, out var v) && int.TryParse(v, out var o) ? Math.Max(0, o) : 0;

        var rows = await db.Playlists
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreationTime).ThenByDescending(p => p.Id)
            .Skip(offset).Take(take + 1)
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility, p.Items.Count(), p.CreationTime))
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

        var playlist = new Playlist
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Slug = await GenerateUniqueSlugAsync(ownerId, request.Name, ct),
            Description = request.Description?.Trim(),
            Visibility = request.Visibility ?? PlaylistVisibility.Private,
        };
        db.Playlists.Add(playlist);
        await db.SaveChangesAsync(ct);

        return playlist.ToResponse(0);
    }

    public async Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var playlist = await db.Playlists
            .Where(p => p.Id == id && p.OwnerId == ownerId)
            .Select(p => new PlaylistResponse(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility, p.Items.Count(), p.CreationTime))
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

        await db.SaveChangesAsync(ct);
        var count = await db.PlaylistItems.CountAsync(i => i.PlaylistId == id, ct);
        return playlist.ToResponse(count);
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
                p.Id, p.Name, p.Slug, p.Description, p.Visibility, p.Items.Count(), p.CreationTime))
            .FirstOrDefaultAsync(ct);

        // Private (or missing) playlists are indistinguishable to anonymous callers.
        return playlist ?? throw new NotFoundException("Playlist not found.");
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
