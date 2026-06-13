using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IPlaylistService
{
    Task<PagedResult<PlaylistResponse>> ListAsync(Guid ownerId, int? limit, string? cursor, CancellationToken ct = default);
    Task<PlaylistResponse> CreateAsync(Guid ownerId, CreatePlaylistRequest request, CancellationToken ct = default);
    Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<PlaylistResponse> UpdateAsync(Guid ownerId, Guid id, UpdatePlaylistRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);
}
