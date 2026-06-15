using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

/// <summary>
/// The private folder system: a per-user tree that organizes the caller's own playlists and
/// public playlists they saved. Every method scopes strictly to <paramref name="ownerId"/>.
/// </summary>
public interface IFolderService
{
    /// <summary>Every folder the caller owns (flat; the client builds the tree from ParentId).</summary>
    Task<IReadOnlyList<FolderResponse>> ListAsync(Guid ownerId, CancellationToken ct = default);

    Task<FolderResponse> CreateAsync(Guid ownerId, CreateFolderRequest request, CancellationToken ct = default);

    /// <summary>A folder's contents: breadcrumbs, direct subfolders, and filed playlists.</summary>
    Task<FolderDetailResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    Task<FolderResponse> RenameAsync(Guid ownerId, Guid id, RenameFolderRequest request, CancellationToken ct = default);

    /// <summary>Reparent a folder (null = root), guarding against cycles and excessive depth.</summary>
    Task<FolderResponse> MoveAsync(Guid ownerId, Guid id, MoveFolderRequest request, CancellationToken ct = default);

    /// <summary>Delete a folder and, recursively, its subfolders and all filed-playlist entries.</summary>
    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>File a playlist into a folder (own playlist of any visibility, or any public one). Moves it if already filed elsewhere.</summary>
    Task AddPlaylistAsync(Guid ownerId, Guid folderId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Remove (unfile/unsave) a playlist from a folder.</summary>
    Task RemovePlaylistAsync(Guid ownerId, Guid folderId, Guid playlistId, CancellationToken ct = default);
}
