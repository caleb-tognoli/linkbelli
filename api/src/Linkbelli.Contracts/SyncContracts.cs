namespace Linkbelli.Contracts;

/// <summary>
/// A playlist as a syncing client needs it: the fields, or a tombstone. Deleted rows are reported
/// rather than simply missing — a client that only ever hears about what exists can never learn
/// that something went away.
/// </summary>
public record SyncedPlaylist(
    Guid Id,
    bool Deleted,
    string? Name,
    string? Slug,
    string? Description,
    string? Visibility,
    string[]? Tags,
    DateTimeOffset LastModified);

/// <summary>An item as a syncing client needs it, with the same tombstone rule.</summary>
public record SyncedItem(
    Guid Id,
    Guid PlaylistId,
    bool Deleted,
    string? Url,
    string? Title,
    string? Note,
    string? Status,
    int? Score,
    string[]? Tags,
    DateTimeOffset LastModified);

/// <summary>
/// Everything that changed since a moment. <c>Until</c> is what to pass as <c>since</c> next
/// time — taken from the server's clock, so a client with a skewed one doesn't skip changes.
/// </summary>
public record SyncResponse(
    DateTimeOffset Until,
    /// <summary>True when the page was capped; call again with the same cursor logic to continue.</summary>
    bool More,
    IReadOnlyList<SyncedPlaylist> Playlists,
    IReadOnlyList<SyncedItem> Items);
