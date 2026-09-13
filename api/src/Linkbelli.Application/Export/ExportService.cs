using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Export;

/// <summary>Hands a user their own data back. Import has existed since the CSV importer; this is the way out.</summary>
public interface IExportService
{
    /// <summary>Everything the caller owns.</summary>
    Task<ExportBundle> ExportAllAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>A single playlist the caller owns, with its items.</summary>
    Task<ExportBundle> ExportPlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);
}

/// <inheritdoc />
public class ExportService(IAppDbContext db, SourceConfigSecrets secrets) : IExportService
{
    public Task<ExportBundle> ExportAllAsync(Guid ownerId, CancellationToken ct = default) =>
        BuildAsync(ownerId, playlistId: null, ct);

    public async Task<ExportBundle> ExportPlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default)
    {
        if (!await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }

        return await BuildAsync(ownerId, playlistId, ct);
    }

    private async Task<ExportBundle> BuildAsync(Guid ownerId, Guid? playlistId, CancellationToken ct)
    {
        var username = await db.Users.Where(u => u.Id == ownerId).Select(u => u.UserName!).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("User not found.");

        var playlistQuery = db.Playlists.Where(p => p.OwnerId == ownerId);
        if (playlistId is not null)
        {
            playlistQuery = playlistQuery.Where(p => p.Id == playlistId);
        }

        var playlists = await playlistQuery
            .OrderBy(p => p.CreationTime)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Description,
                p.Visibility,
                p.CreationTime,
                Tags = p.Tags.Select(t => t.Tag!.Name).ToList(),
                FolderId = db.FolderPlaylists
                    .Where(fp => fp.OwnerId == ownerId && fp.PlaylistId == p.Id)
                    .Select(fp => (Guid?)fp.FolderId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var playlistIds = playlists.Select(p => p.Id).ToList();

        // Every item, in one query — an export is a full dump, so paging it per playlist would
        // just be N round trips for the same rows. Unenriched links are included: they are the
        // user's data whether or not we managed to fetch a title for them.
        var items = await db.PlaylistItems
            .Where(i => playlistIds.Contains(i.PlaylistId))
            .OrderBy(i => i.PlaylistId).ThenBy(i => i.Position)
            .Select(i => new
            {
                i.Id,
                i.PlaylistId,
                i.Note,
                i.Status,
                i.Score,
                i.CreationTime,
                i.Metadata,
                Tags = i.Tags.Select(t => t.Tag!.Name).ToList(),
                Url = i.Link!.CanonicalUrl,
                i.Link.Title,
                i.Link.Description,
                i.Link.ThumbnailUrl,
                i.Link.SiteName,
            })
            .ToListAsync(ct);

        var itemsByPlaylist = items
            .GroupBy(i => i.PlaylistId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ExportItem>)g.Select(i => new ExportItem(
                    i.Id, i.Url, i.Title, i.Description, i.Note, i.Status.ToString(), i.Score,
                    i.ThumbnailUrl, i.SiteName, i.CreationTime, i.Metadata, i.Tags)).ToList());

        var folders = playlistId is null
            ? await db.Folders
                .Where(f => f.OwnerId == ownerId)
                .OrderBy(f => f.Name)
                .Select(f => new ExportFolder(f.Id, f.Name, f.ParentId))
                .ToListAsync(ct)
            : [];

        var sources = playlistId is null ? await ExportSourcesAsync(ownerId, ct) : [];

        return new ExportBundle(
            username,
            DateTimeOffset.UtcNow,
            folders,
            playlists.Select(p => new ExportPlaylist(
                p.Id, p.Name, p.Slug, p.Description, p.Visibility.ToString(), p.Tags, p.FolderId,
                p.CreationTime,
                itemsByPlaylist.TryGetValue(p.Id, out var list) ? list : [])).ToList(),
            sources);
    }

    private async Task<List<ExportSource>> ExportSourcesAsync(Guid ownerId, CancellationToken ct)
    {
        var rows = await db.Sources
            .Where(s => s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Type,
                s.Schedule,
                s.Status,
                s.Visibility,
                s.Config,
                PlaylistIds = s.Playlists.Select(ps => ps.PlaylistId).ToList(),
            })
            .ToListAsync(ct);

        return rows.Select(s =>
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(s.Config) ?? new();
            return new ExportSource(
                s.Id, s.Name, s.Type.ToString(), s.Schedule, s.Status.ToString(), s.Visibility.ToString(),
                // Redacted: an export is a file that gets emailed around, and a scraper's auth
                // header has no business travelling in one.
                secrets.Redact(s.Type, stored),
                s.PlaylistIds);
        }).ToList();
    }
}
