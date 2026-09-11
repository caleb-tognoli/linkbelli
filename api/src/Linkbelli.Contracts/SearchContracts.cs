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
    DateTimeOffset? StatusChangedAt,
    /// <summary>Tags on the link itself.</summary>
    string[] Tags);

/// <summary>Filters a search can be narrowed by, beyond the text itself.</summary>
public record SearchQuery(
    string? Q,
    /// <summary>Restrict to one site, by hostname.</summary>
    string? Host,
    /// <summary>Restrict to playlists carrying every one of these tags.</summary>
    string[]? Tags,
    /// <summary>Restrict to links carrying every one of these tags, whatever list they sit in.</summary>
    string[]? ItemTags,
    /// <summary>"watched" or "unwatched".</summary>
    string? Status,
    /// <summary>Only items scored at least this highly.</summary>
    int? MinScore,
    /// <summary>Only items finished since this moment — "what did I get through this week".</summary>
    DateTimeOffset? FinishedSince,
    /// <summary>Only links whose page is gone or unreadable — the link rot in your collection.</summary>
    bool? Broken,
    /// <summary>"score" for best-rated first; otherwise relevance, or newest when there is no term.</summary>
    string? Sort,
    int? Limit,
    string? Cursor);

/// <summary>A search someone wants to come back to. Membership is whatever matches right now.</summary>
public record SavedSearchResponse(
    Guid Id,
    string Name,
    string? Q,
    string? Host,
    string[] Tags,
    string[] ItemTags,
    string? Status,
    int? MinScore,
    bool Broken,
    string? Sort,
    DateTimeOffset CreationTime);

public record SaveSearchRequest(
    string Name,
    string? Q = null,
    string? Host = null,
    string[]? Tags = null,
    string[]? ItemTags = null,
    string? Status = null,
    int? MinScore = null,
    bool Broken = false,
    string? Sort = null);
