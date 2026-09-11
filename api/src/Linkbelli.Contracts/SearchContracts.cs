using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

/// <summary>
/// One link found by a search across every playlist the caller owns. Carries the playlist it
/// lives in, because "where did I save that" is most of the question being asked.
/// </summary>
public record SearchHit(
    Guid ItemId,
    Guid PlaylistId,
    string PlaylistName,
    LinkResponse Link,
    string? Note,
    PlaylistItemStatus Status,
    int? Score,
    DateTimeOffset AddedAt,
    /// <summary>When the status last changed; null if it never has.</summary>
    DateTimeOffset? StatusChangedAt);

/// <summary>Filters a search can be narrowed by, beyond the text itself.</summary>
public record SearchQuery(
    string? Q,
    /// <summary>Restrict to one site, by hostname.</summary>
    string? Host,
    /// <summary>Restrict to playlists carrying every one of these tags.</summary>
    string[]? Tags,
    /// <summary>"watched" or "unwatched".</summary>
    string? Status,
    /// <summary>Only items scored at least this highly.</summary>
    int? MinScore,
    /// <summary>Only items finished since this moment — "what did I get through this week".</summary>
    DateTimeOffset? FinishedSince,
    int? Limit,
    string? Cursor);
