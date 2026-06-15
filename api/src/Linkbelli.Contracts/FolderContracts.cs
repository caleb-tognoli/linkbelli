using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

// --- Folders (strictly private organization of playlists) ---

public record CreateFolderRequest(string Name, Guid? ParentId);

/// <summary>Rename a folder. Moving is a separate operation (POST /folders/{id}/move).</summary>
public record RenameFolderRequest(string Name);

/// <summary>Reparent a folder; ParentId null moves it to the root.</summary>
public record MoveFolderRequest(Guid? ParentId);

/// <summary>Save/file a playlist into a folder. The playlist may be the caller's own or any public one.</summary>
public record AddFolderPlaylistRequest(Guid PlaylistId);

/// <summary>A folder node, with counts of its direct contents.</summary>
public record FolderResponse(
    Guid Id, string Name, Guid? ParentId, int SubfolderCount, int PlaylistCount, DateTimeOffset CreationTime);

/// <summary>One ancestor on the path from the root to a folder (root-first).</summary>
public record FolderBreadcrumb(Guid Id, string Name);

/// <summary>A playlist filed in a folder. OwnedByMe distinguishes own playlists from saved public ones.</summary>
public record FolderPlaylistResponse(
    Guid PlaylistId, string Name, string Slug, string? Description, PlaylistVisibility Visibility,
    int ItemCount, string[] Tags, bool Nsfw, bool OwnedByMe, string OwnerUsername);

/// <summary>A folder's full contents: ancestor trail, child folders, and filed playlists.</summary>
public record FolderDetailResponse(
    Guid Id, string Name, Guid? ParentId,
    IReadOnlyList<FolderBreadcrumb> Breadcrumbs,
    IReadOnlyList<FolderResponse> Subfolders,
    IReadOnlyList<FolderPlaylistResponse> Playlists);
