namespace Linkbelli.Contracts;

/// <summary>A soft-deleted playlist, restorable until it is purged.</summary>
public record TrashedPlaylist(
    Guid Id, string Name, string Slug, int ItemCount, DateTimeOffset DeletedAt, DateTimeOffset PurgeAfter);

/// <summary>
/// A soft-deleted item, restorable until it is purged. Items whose playlist was itself deleted
/// are not listed separately — they come back with the playlist.
/// </summary>
public record TrashedItem(
    Guid Id, Guid PlaylistId, string PlaylistName, string Url, string? Title,
    DateTimeOffset DeletedAt, DateTimeOffset PurgeAfter);

/// <summary>Everything the caller can still get back, newest deletion first.</summary>
public record TrashResponse(
    IReadOnlyList<TrashedPlaylist> Playlists,
    IReadOnlyList<TrashedItem> Items,
    int RetentionDays);
