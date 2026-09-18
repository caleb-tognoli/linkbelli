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
    IReadOnlyList<ExportSource> Sources,
    /// <summary>
    /// The shape of this file.
    /// </summary>
    /// <remarks>
    /// Absent in everything written before restoring existed, which is exactly why it is here:
    /// a reader has to be able to tell an old file from a new one without guessing, and a file
    /// written today has to still be readable by a version that has learned more fields.
    ///
    /// <see cref="ExportFormatVersion.Current"/> says what this is now and what changed.
    /// </remarks>
    int Version = ExportFormatVersion.Current,
    /// <summary>
    /// Passages marked in saved articles. Held at the top level rather than on each item, because
    /// a highlight belongs to an article and an article can sit in more than one playlist —
    /// hanging it off items would write it out once per copy and restore it as duplicates.
    /// </summary>
    IReadOnlyList<ExportHighlight>? Highlights = null);

/// <summary>What each version of the export format added.</summary>
public static class ExportFormatVersion
{
    /// <summary>
    /// The shape written before there was a version field at all. A file with no
    /// <c>version</c> is one of these.
    /// </summary>
    public const int Original = 1;

    /// <summary>Item tags, which the original dropped on the floor.</summary>
    public const int WithItemTags = 2;

    /// <summary>Highlights and their notes.</summary>
    public const int WithHighlights = 3;

    public const int Current = WithHighlights;
}

/// <summary>
/// A marked passage, named by the article's address rather than by an id: the file has to make
/// sense on an instance where none of the ids it was written with exist.
/// </summary>
public record ExportHighlight(
    string Url,
    int ParagraphIndex,
    int Start,
    int End,
    string Text,
    string? Note,
    DateTimeOffset CreatedAt);

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
    IReadOnlyDictionary<string, string>? Metadata,
    /// <summary>
    /// Tags on the link itself. Empty in a file written before version 2, which did not carry
    /// them at all — so an export was never quite everything, and a restore from one is not.
    /// </summary>
    IReadOnlyList<string>? Tags = null);

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
