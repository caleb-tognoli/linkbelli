using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IPlaylistItemService
{
    Task<PagedResult<PlaylistItemResponse>> ListAsync(Guid ownerId, Guid playlistId, int? limit, string? cursor, CancellationToken ct = default);
    Task<PlaylistItemResponse> AddAsync(Guid ownerId, Guid playlistId, AddItemRequest request, CancellationToken ct = default);
    Task<PlaylistItemResponse> UpdateAsync(Guid ownerId, Guid itemId, UpdateItemRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);
    Task<PlaylistItemResponse> MoveAsync(Guid ownerId, Guid itemId, MoveItemRequest request, CancellationToken ct = default);

    /// <summary>Anonymous read of items in a non-private playlist (owner username + slug). Respects the viewer's NSFW preference.</summary>
    Task<PagedResult<PlaylistItemResponse>> ListPublicAsync(string username, string slug, int? limit, string? cursor, Guid? viewerId, CancellationToken ct = default);
}
