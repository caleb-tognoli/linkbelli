using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

// --- Playlists ---
public record CreatePlaylistRequest(string Name, string? Description, PlaylistVisibility? Visibility);

public record UpdatePlaylistRequest(string? Name, string? Description, PlaylistVisibility? Visibility);

public record PlaylistResponse(
    Guid Id, string Name, string Slug, string? Description,
    PlaylistVisibility Visibility, int ItemCount, DateTimeOffset CreationTime);

// --- Items ---
public record AddItemRequest(string Url, string? Note);

public record UpdateItemRequest(string? Note);

/// <summary>Place the item immediately after AfterItemId; null moves it to the front.</summary>
public record MoveItemRequest(Guid? AfterItemId);

public record PlaylistItemResponse(
    Guid Id, long Position, string? Note, PlaylistItemStatus Status, LinkResponse Link, DateTimeOffset CreationTime);

// --- Links ---
public record CreateLinkRequest(string Url);

public record LinkResponse(
    Guid Id, string Url, string Host, string? Title, string? Description,
    string? ThumbnailUrl, string? SiteName, bool Enriched);
