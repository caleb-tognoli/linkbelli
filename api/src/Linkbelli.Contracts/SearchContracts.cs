using Linkbelli.Core.Content;
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
    string[] Tags,
    /// <summary>
    /// Where the term was found in the article text, when it was only found there. Null for a
    /// hit on the title or the note, where the reason it matched is already on screen.
    /// </summary>
    string? Snippet = null,
    /// <summary>How far through the article this is, 0 to 1. Null until it has been opened.</summary>
    double? ReadProgress = null,
    /// <summary>Put aside until this moment. Null when it is not.</summary>
    DateTimeOffset? SnoozedUntil = null,
    /// <summary>How many times it has been put aside.</summary>
    int SnoozeCount = 0);

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
    /// <summary>Restrict to one kind: article, video, repository, paper, document, audio, image, social.</summary>
    string? Kind,
    /// <summary>
    /// Only articles that can be read in this many minutes or fewer — which is how people
    /// actually pick what to open next.
    /// </summary>
    int? MaxMinutes,
    /// <summary>"score" for best-rated first; otherwise relevance, or newest when there is no term.</summary>
    string? Sort,
    int? Limit,
    string? Cursor,
    /// <summary>
    /// True to see only what is put aside and not yet due. Anything else hides those, which is
    /// what "not now" has to mean if the button is to be worth pressing.
    /// </summary>
    bool? Snoozed = null,
    /// <summary>True to see only what has a passage marked in it.</summary>
    bool? Highlighted = null);

/// <summary>A pinned search and how many things match it right now.</summary>
public record PinnedSearch(Guid Id, string Name, int Count);

/// <summary>Keep a saved search in the sidebar, or take it out.</summary>
public record PinSearchRequest(bool Pinned);

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
    DateTimeOffset CreationTime,
    /// <summary>Restrict to one kind of thing.</summary>
    string? Kind = null,
    /// <summary>Only what can be read in this many minutes.</summary>
    int? MaxMinutes = null,
    /// <summary>Whether this one is kept in the sidebar.</summary>
    bool Pinned = false);

public record SaveSearchRequest(
    string Name,
    string? Q = null,
    string? Host = null,
    string[]? Tags = null,
    string[]? ItemTags = null,
    string? Status = null,
    int? MinScore = null,
    bool Broken = false,
    string? Sort = null,
    string? Kind = null,
    int? MaxMinutes = null);
