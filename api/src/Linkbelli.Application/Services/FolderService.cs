using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class FolderService(IAppDbContext db, IUserPreferenceService prefs) : IFolderService
{
    /// <summary>Bounds nesting so the tree stays renderable and traversals stay cheap.</summary>
    public const int MaxDepth = 10;
    private const int MaxNameLength = 200;

    public async Task<IReadOnlyList<FolderResponse>> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        var folders = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync(ct);
        var counts = await PlaylistCountsAsync(ownerId, ct);
        return folders
            .OrderBy(f => f.Name)
            .Select(f => ToResponse(f, folders, counts))
            .ToList();
    }

    public async Task<FolderResponse> CreateAsync(Guid ownerId, CreateFolderRequest request, CancellationToken ct = default)
    {
        var name = ValidateName(request.Name);

        if (request.ParentId is { } parentId)
        {
            var parent = await OwnedFolderAsync(ownerId, parentId, ct);
            if (await DepthAsync(ownerId, parent) + 1 > MaxDepth)
            {
                throw new ValidationException("parentId", $"Folders may not be nested more than {MaxDepth} levels deep.");
            }
        }

        var folder = new Folder { OwnerId = ownerId, Name = name, ParentId = request.ParentId };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);

        return new FolderResponse(folder.Id, folder.Name, folder.ParentId, 0, 0, folder.CreationTime);
    }

    public async Task<FolderDetailResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var folders = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync(ct);
        var folder = folders.FirstOrDefault(f => f.Id == id) ?? throw new NotFoundException("Folder not found.");

        var counts = await PlaylistCountsAsync(ownerId, ct);
        var byId = folders.ToDictionary(f => f.Id);

        // Breadcrumbs: walk up the parent chain, then present root-first (ancestors only).
        var trail = new List<FolderBreadcrumb>();
        var cursor = folder.ParentId;
        while (cursor is { } pid && byId.TryGetValue(pid, out var ancestor))
        {
            trail.Add(new FolderBreadcrumb(ancestor.Id, ancestor.Name));
            cursor = ancestor.ParentId;
        }

        trail.Reverse();

        var subfolders = folders
            .Where(f => f.ParentId == id)
            .OrderBy(f => f.Name)
            .Select(f => ToResponse(f, folders, counts))
            .ToList();

        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        // Filed playlists: the caller's own (any visibility) or a public one they saved. A saved
        // public playlist that later went private becomes inaccessible, so it is hidden here.
        var entries = from fp in db.FolderPlaylists
                      where fp.OwnerId == ownerId && fp.FolderId == id
                      join p in db.Playlists on fp.PlaylistId equals p.Id // soft-deleted playlists drop out
                      join u in db.Users on p.OwnerId equals u.Id
                      where p.OwnerId == ownerId || p.Visibility == PlaylistVisibility.Public
                      select new { p, OwnerUsername = u.UserName! };

        if (!showNsfw)
        {
            entries = entries.Where(x => !x.p.Items.Any(i => i.Link!.Nsfw));
        }

        var playlists = await entries
            .OrderByDescending(x => x.p.CreationTime)
            .Select(x => new FolderPlaylistResponse(
                x.p.Id, x.p.Name, x.p.Slug, x.p.Description, x.p.Visibility,
                x.p.Items.Count(i => i.Link!.EnrichedAt != null),
                x.p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                x.p.Items.Any(i => i.Link!.Nsfw),
                x.p.OwnerId == ownerId, x.OwnerUsername))
            .ToListAsync(ct);

        return new FolderDetailResponse(folder.Id, folder.Name, folder.ParentId, trail, subfolders, playlists);
    }

    public async Task<FolderResponse> RenameAsync(Guid ownerId, Guid id, RenameFolderRequest request, CancellationToken ct = default)
    {
        var folder = await OwnedFolderAsync(ownerId, id, ct);
        folder.Name = ValidateName(request.Name);
        await db.SaveChangesAsync(ct);
        return await SingleResponseAsync(ownerId, folder, ct);
    }

    public async Task<FolderResponse> MoveAsync(Guid ownerId, Guid id, MoveFolderRequest request, CancellationToken ct = default)
    {
        var folder = await OwnedFolderAsync(ownerId, id, ct);
        var newParentId = request.ParentId;

        if (newParentId == id)
        {
            throw new ValidationException("parentId", "A folder cannot be its own parent.");
        }

        var folders = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync(ct);

        if (newParentId is { } parentId)
        {
            var parent = folders.FirstOrDefault(f => f.Id == parentId)
                ?? throw new ValidationException("parentId", "Destination folder not found.");

            // Reject moving a folder under itself or one of its own descendants (would form a cycle).
            if (IsDescendantOf(folders, parentId, id))
            {
                throw new ValidationException("parentId", "A folder cannot be moved into its own subtree.");
            }

            // After the move the folder sits one below its new parent, and its deepest descendant
            // sits parentDepth + subtreeHeight levels down.
            var parentDepth = DepthOf(folders, parent); // root = 1
            if (parentDepth + SubtreeHeight(folders, id) > MaxDepth)
            {
                throw new ValidationException("parentId", $"That move would nest folders more than {MaxDepth} levels deep.");
            }
        }

        folder.ParentId = newParentId;
        await db.SaveChangesAsync(ct);
        return await SingleResponseAsync(ownerId, folder, ct);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var folders = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync(ct);
        if (folders.All(f => f.Id != id))
        {
            throw new NotFoundException("Folder not found.");
        }

        // Soft delete intercepts hard deletes (no DB cascade fires), so recurse manually:
        // collect the folder and every descendant, then remove their entries and the folders.
        var toRemove = DescendantsAndSelf(folders, id);
        var entries = await db.FolderPlaylists
            .Where(fp => fp.OwnerId == ownerId && toRemove.Contains(fp.FolderId))
            .ToListAsync(ct);
        db.FolderPlaylists.RemoveRange(entries);
        db.Folders.RemoveRange(folders.Where(f => toRemove.Contains(f.Id)));
        await db.SaveChangesAsync(ct);
    }

    public async Task AddPlaylistAsync(Guid ownerId, Guid folderId, Guid playlistId, CancellationToken ct = default)
    {
        _ = await OwnedFolderAsync(ownerId, folderId, ct);

        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId, ct);
        // Own playlists may be filed regardless of visibility; others' only if public.
        if (playlist is null || (playlist.OwnerId != ownerId && playlist.Visibility != PlaylistVisibility.Public))
        {
            throw new NotFoundException("Playlist not found.");
        }

        var existing = await db.FolderPlaylists
            .FirstOrDefaultAsync(fp => fp.OwnerId == ownerId && fp.PlaylistId == playlistId, ct);
        if (existing is not null)
        {
            existing.FolderId = folderId; // move it to the requested folder (idempotent if unchanged)
            await db.SaveChangesAsync(ct);
            return;
        }

        db.FolderPlaylists.Add(new FolderPlaylist { OwnerId = ownerId, FolderId = folderId, PlaylistId = playlistId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race creating the (owner, playlist) entry — treat as already filed (idempotent).
        }
    }

    public async Task RemovePlaylistAsync(Guid ownerId, Guid folderId, Guid playlistId, CancellationToken ct = default)
    {
        _ = await OwnedFolderAsync(ownerId, folderId, ct);

        var entries = await db.FolderPlaylists
            .Where(fp => fp.OwnerId == ownerId && fp.FolderId == folderId && fp.PlaylistId == playlistId)
            .ToListAsync(ct);
        if (entries.Count > 0)
        {
            db.FolderPlaylists.RemoveRange(entries);
            await db.SaveChangesAsync(ct);
        }
    }

    // --- helpers ---

    private async Task<Folder> OwnedFolderAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        await db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.OwnerId == ownerId, ct)
        ?? throw new NotFoundException("Folder not found.");

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationException("name", "Name is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationException("name", $"Name must be {MaxNameLength} characters or fewer.");
        }

        return trimmed;
    }

    private async Task<Dictionary<Guid, int>> PlaylistCountsAsync(Guid ownerId, CancellationToken ct) =>
        await db.FolderPlaylists
            .Where(fp => fp.OwnerId == ownerId)
            .GroupBy(fp => fp.FolderId)
            .Select(g => new { FolderId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.FolderId, x => x.Count, ct);

    private static FolderResponse ToResponse(Folder folder, IReadOnlyList<Folder> all, IReadOnlyDictionary<Guid, int> playlistCounts) =>
        new(folder.Id, folder.Name, folder.ParentId,
            all.Count(f => f.ParentId == folder.Id),
            playlistCounts.GetValueOrDefault(folder.Id),
            folder.CreationTime);

    private async Task<FolderResponse> SingleResponseAsync(Guid ownerId, Folder folder, CancellationToken ct)
    {
        var folders = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync(ct);
        var counts = await PlaylistCountsAsync(ownerId, ct);
        return ToResponse(folder, folders, counts);
    }

    // --- in-memory tree traversals over the owner's (small) folder set ---

    private static int DepthOf(IReadOnlyList<Folder> all, Folder folder)
    {
        var byId = all.ToDictionary(f => f.Id);
        var depth = 1;
        var cursor = folder.ParentId;
        while (cursor is { } pid && byId.TryGetValue(pid, out var parent))
        {
            depth++;
            cursor = parent.ParentId;
        }

        return depth;
    }

    private async Task<int> DepthAsync(Guid ownerId, Folder folder)
    {
        var all = await db.Folders.Where(f => f.OwnerId == ownerId).ToListAsync();
        return DepthOf(all, folder);
    }

    /// <summary>True if <paramref name="candidateId"/> is <paramref name="ancestorId"/> or sits under it.</summary>
    private static bool IsDescendantOf(IReadOnlyList<Folder> all, Guid candidateId, Guid ancestorId)
    {
        if (candidateId == ancestorId)
        {
            return true;
        }

        var byId = all.ToDictionary(f => f.Id);
        var cursor = byId.TryGetValue(candidateId, out var node) ? node.ParentId : null;
        while (cursor is { } pid && byId.TryGetValue(pid, out var parent))
        {
            if (pid == ancestorId)
            {
                return true;
            }

            cursor = parent.ParentId;
        }

        return false;
    }

    private static int SubtreeHeight(IReadOnlyList<Folder> all, Guid rootId)
    {
        var children = all.Where(f => f.ParentId == rootId).ToList();
        return children.Count == 0 ? 1 : 1 + children.Max(c => SubtreeHeight(all, c.Id));
    }

    private static HashSet<Guid> DescendantsAndSelf(IReadOnlyList<Folder> all, Guid rootId)
    {
        var result = new HashSet<Guid> { rootId };
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rootId);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var child in all.Where(f => f.ParentId == current))
            {
                if (result.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }

        return result;
    }
}
