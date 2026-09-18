namespace Linkbelli.Contracts;

/// <summary>
/// A passage somebody marked, as a client sees it.
/// </summary>
/// <remarks>
/// <see cref="Orphaned"/> is the interesting field. An article can be enriched again and its
/// paragraphs shift underneath a highlight, so a client is told plainly when the offsets no
/// longer find the quote rather than being left to draw a mark in the wrong place.
/// </remarks>
public record HighlightResponse(
    Guid Id,
    Guid LinkId,
    int ParagraphIndex,
    int Start,
    int End,
    string Text,
    string? Note,
    DateTimeOffset CreatedAt,
    bool Orphaned = false);

/// <summary>A passage marked in an article.</summary>
public record CreateHighlightRequest(
    int ParagraphIndex,
    int Start,
    int End,
    string Text,
    string? Note = null);

/// <summary>
/// What the reader had to say about a passage. Only the note is editable — the passage itself
/// is a quotation, and editing a quotation is a different thing from making one.
/// </summary>
public record UpdateHighlightRequest(string? Note);

/// <summary>
/// One highlight with enough of its article to be worth reading on its own.
/// </summary>
/// <remarks>
/// The list of everything somebody has marked is the highest-signal text in their library, and a
/// bare quote with no idea where it came from is close to useless on that screen.
/// </remarks>
public record HighlightWithSourceResponse(
    Guid Id,
    Guid LinkId,
    string Url,
    string? Title,
    string? SiteName,
    string Host,
    string Text,
    string? Note,
    int ParagraphIndex,
    DateTimeOffset CreatedAt);
