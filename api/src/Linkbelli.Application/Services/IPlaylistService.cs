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

    /// <summary>Anonymous read of a non-private playlist by owner username + slug. NSFW playlists 404 unless the viewer opted in.</summary>
    Task<PlaylistResponse> GetPublicAsync(string username, string slug, Guid? viewerId, CancellationToken ct = default);

    /// <summary>
    /// Saves how the caller looks at one of their playlists. Replaces the whole view, so a
    /// client sends the state it wants rather than a patch.
    /// </summary>
    Task SaveViewAsync(Guid ownerId, Guid playlistId, PlaylistViewPreferences view, CancellationToken ct = default);

    /// <summary>
    /// A user as seen from the outside. Throws NotFoundException for an unknown username — a
    /// profile that doesn't exist and one you can't see look the same.
    /// </summary>
    Task<PublicProfile> GetPublicProfileAsync(string username, Guid? viewerId, CancellationToken ct = default);

    /// <summary>One user's public playlists, newest first.</summary>
    Task<PagedResult<PublicPlaylistSummary>> ListUserPublicPlaylistsAsync(
        string username, int? limit, string? cursor, Guid? viewerId, CancellationToken ct = default);

    /// <summary>Tags used across the caller's own playlists, with counts (autocomplete/management).</summary>
    Task<IReadOnlyList<TagSummary>> ListOwnTagsAsync(Guid ownerId, string? q, CancellationToken ct = default);

    /// <summary>Tags used across public playlists, with counts (global discovery tag cloud).</summary>
    Task<IReadOnlyList<TagSummary>> ListPublicTagsAsync(string? q, CancellationToken ct = default);

    /// <summary>Every public playlist's address and date, for a sitemap.</summary>
    Task<PagedResult<SitemapEntry>> ListForSitemapAsync(
        int? limit, string? cursor, CancellationToken ct = default);

    /// <summary>Attach a source (the caller's own, or any shared one) to a playlist the caller owns. Returns the total number of items ever discovered by the source (for backfill prompt).</summary>
    Task<int> SubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Add all links ever discovered by a source into a playlist the caller owns. Returns count of items added.</summary>
    Task<int> BackfillFromSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Detach a source from a playlist the caller owns.</summary>
    Task UnsubscribeSourceAsync(Guid ownerId, Guid playlistId, Guid sourceId, CancellationToken ct = default);

    /// <summary>Sources currently attached to a playlist the caller owns.</summary>
    Task<IReadOnlyList<AttachedSourceSummary>> ListAttachedSourcesAsync(Guid ownerId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Shared sources attached to a public (non-private) playlist — safe for anonymous callers.</summary>
    Task<IReadOnlyList<AttachedSourceSummary>> ListPublicAttachedSourcesAsync(string username, string slug, CancellationToken ct = default);
    /// <summary>
    /// Likes a playlist the caller can see. Idempotent: liking twice is one like, because a
    /// double tap should not be a way to inflate a number.
    /// </summary>
    Task<PlaylistLikeResponse> LikeAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Takes the like back. Also idempotent.</summary>
    Task<PlaylistLikeResponse> UnlikeAsync(Guid userId, Guid playlistId, CancellationToken ct = default);

    /// <summary>
    /// Discovery of public playlists, filtered by name and tag and the viewer's NSFW preference,
    /// in a given order: "active", "liked", "largest", or newest when nothing is asked for.
    /// </summary>
    Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(
        string? q, string[]? tags, string? sort, int? limit, string? cursor, Guid? viewerId,
        CancellationToken ct = default);

    /// <summary>Public playlists like this one — sharing its tags, or holding the same links.</summary>
    Task<IReadOnlyList<PublicPlaylistSummary>> ListSimilarAsync(
        string username, string slug, int? limit, Guid? viewerId, CancellationToken ct = default);

    /// <summary>Tags on public playlists that have seen activity lately.</summary>
    Task<IReadOnlyList<TagSummary>> ListTrendingTagsAsync(int? days, CancellationToken ct = default);
}
