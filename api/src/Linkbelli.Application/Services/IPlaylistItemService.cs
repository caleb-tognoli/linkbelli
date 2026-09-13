using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IPlaylistItemService
{
    Task<PagedResult<PlaylistItemResponse>> ListAsync(Guid ownerId, Guid playlistId, int? limit, string? cursor, string? sort, string? source, string? status, string? q, CancellationToken ct = default);
    Task<PlaylistItemResponse> AddAsync(Guid ownerId, Guid playlistId, AddItemRequest request, CancellationToken ct = default);
    Task<PlaylistItemResponse> UpdateAsync(Guid ownerId, Guid itemId, UpdateItemRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);
    Task<PlaylistItemResponse> MoveAsync(Guid ownerId, Guid itemId, MoveItemRequest request, CancellationToken ct = default);
    Task<PlaylistItemResponse> SetScoreAsync(Guid ownerId, Guid itemId, int? score, CancellationToken ct = default);

    /// <summary>
    /// Puts an item aside until a moment, or brings it back when that moment is null.
    /// </summary>
    /// <remarks>
    /// <paramref name="now"/> comes from the caller so a preset lands in their evening rather
    /// than in UTC's. A snoozed item is out of the queue and out of search until it is due.
    /// </remarks>
    Task<PlaylistItemResponse> SnoozeAsync(
        Guid ownerId, Guid itemId, DateTimeOffset? until, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Anonymous read of items in a non-private playlist (owner username + slug). Respects the viewer's NSFW preference.</summary>
    Task<PagedResult<PlaylistItemResponse>> ListPublicAsync(string username, string slug, int? limit, string? cursor, string? sort, string? source, string? status, string? q, Guid? viewerId, CancellationToken ct = default);
}
