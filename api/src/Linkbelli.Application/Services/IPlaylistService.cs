using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IPlaylistService
{
    /// <summary>List the caller's playlists. When <paramref name="unfiled"/> is true, only playlists not filed in any folder (the home "root" view).</summary>
    Task<PagedResult<PlaylistResponse>> ListAsync(Guid ownerId, int? limit, string? cursor, string[]? tags, string? q = null, bool unfiled = false, CancellationToken ct = default);
    Task<PlaylistResponse> CreateAsync(Guid ownerId, CreatePlaylistRequest request, CancellationToken ct = default);
    Task<PlaylistResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<PlaylistResponse> UpdateAsync(Guid ownerId, Guid id, UpdatePlaylistRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Saves how the caller looks at one of their playlists. Replaces the whole view, so a
    /// client sends the state it wants rather than a patch.
    /// </summary>
    Task SaveViewAsync(Guid ownerId, Guid playlistId, PlaylistViewPreferences view, CancellationToken ct = default);

    /// <summary>Tags used across the caller's own playlists, with counts (autocomplete/management).</summary>
    Task<IReadOnlyList<TagSummary>> ListOwnTagsAsync(Guid ownerId, string? q, CancellationToken ct = default);

    /// <summary>
    /// Takes a copy of somebody's public playlist into the caller's own library.
    /// </summary>
    /// <remarks>
    /// Discovery exists to put a list you want in front of you, and until now the only things you
    /// could do with one were follow it — a stream of what it gains next, not the thing you just
    /// found — or copy the links one at a time.
    /// </remarks>
    Task<PlaylistResponse> ForkAsync(
        Guid ownerId, string username, string slug, CancellationToken ct = default);
}
