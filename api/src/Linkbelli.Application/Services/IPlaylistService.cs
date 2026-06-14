using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IPlaylistService
{
    Task<PagedResult<PlaylistResponse>> ListAsync(Guid ownerId, int? limit, string? cursor, string[]? tags, CancellationToken ct = default);
    Task<PlaylistResponse> CreateAsync(Guid ownerId, CreatePlaylistRequest request, CancellationToken ct = default);
    Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<PlaylistResponse> UpdateAsync(Guid ownerId, Guid id, UpdatePlaylistRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>Anonymous read of a non-private playlist addressed by owner username + slug.</summary>
    Task<PlaylistResponse> GetPublicAsync(string username, string slug, CancellationToken ct = default);

    /// <summary>Anonymous discovery of public playlists, optionally filtered by name query and/or tag.</summary>
    Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(string? q, string[]? tags, int? limit, string? cursor, CancellationToken ct = default);

    /// <summary>Tags used across the caller's own playlists, with counts (autocomplete/management).</summary>
    Task<IReadOnlyList<TagSummary>> ListOwnTagsAsync(Guid ownerId, string? q, CancellationToken ct = default);

    /// <summary>Tags used across public playlists, with counts (global discovery tag cloud).</summary>
    Task<IReadOnlyList<TagSummary>> ListPublicTagsAsync(string? q, CancellationToken ct = default);

    /// <summary>Attach a source (the caller's own, or any shared one) to a playlist the caller owns.</summary>
    Task SubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Detach a source from a playlist the caller owns.</summary>
    Task UnsubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Sources currently attached to a playlist the caller owns.</summary>
    Task<IReadOnlyList<AttachedSourceSummary>> ListAttachedSourcesAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);
}
