using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

// --- Playlists ---
public record CreatePlaylistRequest(string Name, string? Description, PlaylistVisibility? Visibility, string[]? Tags);

/// <summary>The owner's answer on whether a playlist is adult.</summary>
public enum NsfwSetting
{
    /// <summary>Work it out from the items — the default.</summary>
    Auto = 0,
    Yes = 1,
    No = 2,
}

public record UpdatePlaylistRequest(
    string? Name, string? Description, PlaylistVisibility? Visibility, string[]? Tags,
    /// <summary>Override the automatic adult-content reading. Omit to leave it as it is.</summary>
    NsfwSetting? Nsfw = null);

public record PlaylistResponse(
    Guid Id, string Name, string Slug, string? Description,
    PlaylistVisibility Visibility, int ItemCount, DateTimeOffset CreationTime, string[] Tags, bool Nsfw,
    Guid? FolderId = null, string? FolderName = null,
    /// <summary>
    /// Whether the owner set the adult flag by hand, or left it automatic. Null in listings,
    /// which don't report it — rather than defaulting to Auto and misreporting an override.
    /// </summary>
    NsfwSetting? NsfwSetting = null,
    /// <summary>
    /// Links added but not yet fetched. They aren't listed or counted until enrichment finishes,
    /// so without this the item count of a freshly filled playlist just creeps upward on its own.
    /// Null on reads that don't report it (a visitor can't do anything about it).
    /// </summary>
    int? PendingCount = null,
    /// <summary>Mean of the scores that were given, or null when nothing here is rated.</summary>
    double? AverageScore = null,
    /// <summary>How many items carry a score. Without it an average says nothing about its weight.</summary>
    int? ScoredCount = null,
    /// <summary>How the caller last looked at this playlist. Null when they have no saved view.</summary>
    PlaylistViewPreferences? View = null);

/// <summary>
/// How one person looks at one playlist: sort, filters, and what the rows show. Saved per
/// account rather than per browser, so it follows them between devices.
/// </summary>
public record PlaylistViewPreferences(
    string? Sort,
    string? Source,
    string? Status,
    bool ShowUrls,
    bool ShowThumbnails);

/// <summary>A public playlist as surfaced by discovery; deep-links via owner username + slug.</summary>
public record PublicPlaylistSummary(
    string OwnerUsername, string Slug, string Name, string? Description,
    int ItemCount, DateTimeOffset CreationTime, string[] Tags, bool Nsfw);

/// <summary>
/// A user as seen from the outside: who they are and what they have published. Deliberately
/// thin — email and anything else private never appears here.
/// </summary>
public record PublicProfile(
    string Username,
    DateTimeOffset JoinedAt,
    int PublicPlaylistCount,
    int PublicItemCount);

/// <summary>A tag and how many playlists carry it (within the queried scope).</summary>
public record TagSummary(string Name, int PlaylistCount);

// --- Items ---
public record AddItemRequest(string Url, string? Note);

public record UpdateItemRequest(string? Note, PlaylistItemStatus? Status = null);

public record SetScoreRequest(int? Score);

/// <summary>Place the item immediately after AfterItemId; null moves it to the front.</summary>
public record MoveItemRequest(Guid? AfterItemId);

public record PlaylistItemResponse(
    Guid Id, long Position, string? Note, PlaylistItemStatus Status, LinkResponse Link, DateTimeOffset CreationTime,
    IReadOnlyDictionary<string, string>? Metadata = null, Guid? SourceId = null, int? Score = null,
    /// <summary>When the status last changed; null if it never has.</summary>
    DateTimeOffset? StatusChangedAt = null);

// --- Links ---
public record CreateLinkRequest(string Url);

public record LinkResponse(
    Guid Id, string Url, string Host, string? Title, string? Description,
    string? ThumbnailUrl, string? SiteName, bool Enriched, bool Nsfw,
    /// <summary>The site's favicon, shared by every link on that host. Null until a link there has been enriched.</summary>
    string? Favicon = null,
    /// <summary>How the last fetch went: Pending, Succeeded, Failed or Broken.</summary>
    EnrichmentStatus EnrichmentStatus = EnrichmentStatus.Pending,
    /// <summary>Why the last fetch failed, phrased for a reader. Null when it didn't.</summary>
    string? EnrichmentError = null);

/// <summary>Metadata fetched for a URL without saving anything (paste → preview → confirm).</summary>
public record LinkPreviewResponse(
    string CanonicalUrl, string Host, string? Title, string? Description, string? ImageUrl, string? SiteName);
