using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

/// <summary>
/// Recovery for soft-deleted playlists and items. Deletion has always stamped a DeletionTime
/// and kept the row; this is the way back to it, and the thing that eventually clears it out.
/// </summary>
public interface ITrashService
{
    /// <summary>Everything the caller deleted that is still inside the retention window.</summary>
    Task<TrashResponse> ListAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>Restores a deleted playlist, along with the items that were deleted with it.</summary>
    Task RestorePlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Restores a single deleted item into its (live) playlist.</summary>
    Task RestoreItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);

    /// <summary>Permanently removes everything currently in the caller's trash.</summary>
    Task<int> EmptyAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>Permanently removes one deleted playlist, with everything that hangs off it.</summary>
    Task PurgePlaylistAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Permanently removes one deleted item.</summary>
    Task PurgeItemAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes rows soft-deleted longer ago than the retention window, across all
    /// users. Run on a schedule; returns how many rows were removed.
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken ct = default);
}
