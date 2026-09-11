using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

// --- Playlists ---
public record CreatePlaylistRequest(string Name, string? Description, PlaylistVisibility? Visibility, string[]? Tags);

public record UpdatePlaylistRequest(string? Name, string? Description, PlaylistVisibility? Visibility, string[]? Tags);

public record PlaylistResponse(
    Guid Id, string Name, string Slug, string? Description,
    PlaylistVisibility Visibility, int ItemCount, DateTimeOffset CreationTime, string[] Tags, bool Nsfw,
    Guid? FolderId = null, string? FolderName = null);

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
