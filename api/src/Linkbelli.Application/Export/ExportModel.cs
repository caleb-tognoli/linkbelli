namespace Linkbelli.Application.Export;

/// <summary>
/// A snapshot of everything one user owns, format-independent. The serializers turn this into
/// JSON, CSV, a bookmark file or OPML without touching the database again.
/// </summary>
public record ExportBundle(
    string Username,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportFolder> Folders,
    IReadOnlyList<ExportPlaylist> Playlists,
    IReadOnlyList<ExportSource> Sources);

public record ExportFolder(Guid Id, string Name, Guid? ParentId);

public record ExportPlaylist(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string Visibility,
    IReadOnlyList<string> Tags,
    Guid? FolderId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ExportItem> Items);

public record ExportItem(
    Guid Id,
    string Url,
    string? Title,
    string? Description,
    string? Note,
    string Status,
    int? Score,
    string? ThumbnailUrl,
    string? SiteName,
    DateTimeOffset AddedAt,
    IReadOnlyDictionary<string, string>? Metadata);

/// <summary>
/// A source as exported. Config secrets are redacted — an export is a file that gets emailed
/// around, and a scraper's auth header has no business travelling in one.
/// </summary>
public record ExportSource(
    Guid Id,
    string Name,
    string Type,
    string Schedule,
    string Status,
    string Visibility,
    IReadOnlyDictionary<string, string> Config,
    IReadOnlyList<Guid> PlaylistIds);
