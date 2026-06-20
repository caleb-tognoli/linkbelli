using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class ImportService(IAppDbContext db, ILinkService links, IPlaylistService playlists) : IImportService
{
    public async Task<ImportResult> ImportAsync(Guid ownerId, ImportRequest request, CancellationToken ct = default)
    {
        if (request.Rows.Length == 0)
            return new ImportResult(0, 0, []);

        Guid? playlistId = request.PlaylistId;

        if (!string.IsNullOrWhiteSpace(request.NewPlaylistName))
        {
            var created = await playlists.CreateAsync(ownerId,
                new CreatePlaylistRequest(request.NewPlaylistName.Trim(), null, null, null), ct);
            playlistId = created.Id;
        }

        if (playlistId is not null &&
            !await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }

        int imported = 0, skipped = 0;
        var errors = new List<string>();

        // Pre-load existing link IDs in this playlist to detect duplicates without per-row queries.
        HashSet<Guid>? existingLinkIds = null;
        long maxPos = 0;
        if (playlistId is not null)
        {
            existingLinkIds = (await db.PlaylistItems
                .Where(i => i.PlaylistId == playlistId)
                .Select(i => i.LinkId)
                .ToListAsync(ct)).ToHashSet();

            maxPos = await db.PlaylistItems
                .Where(i => i.PlaylistId == playlistId)
                .MaxAsync(i => (long?)i.Position, ct) ?? 0;
        }

        foreach (var row in request.Rows)
        {
            if (!UrlCanonicalizer.TryCanonicalize(row.Url, out var canonical))
            {
                errors.Add($"Invalid URL: {row.Url}");
                continue;
            }

            Link link;
            try
            {
                link = await links.GetOrCreateAsync(canonical, immediate: false, ct);
            }
            catch (BlockedHostException ex)
            {
                errors.Add($"{row.Url}: {ex.Message}");
                continue;
            }

            if (playlistId is null)
            {
                // No playlist destination — the link is still created in the global pool.
                imported++;
                continue;
            }

            if (existingLinkIds!.Contains(link.Id))
            {
                skipped++;
                continue;
            }

            maxPos += PlaylistOrdering.Gap;
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlistId.Value,
                LinkId = link.Id,
                Position = maxPos,
                Note = row.Note?.Trim(),
                Status = PlaylistItemStatus.Active,
            });
            existingLinkIds.Add(link.Id);
            imported++;
        }

        if (playlistId is not null && imported > 0)
            await db.SaveChangesAsync(ct);

        return new ImportResult(imported, skipped, errors);
    }
}
