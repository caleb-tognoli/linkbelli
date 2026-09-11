using Linkbelli.Core.Content;

namespace Linkbelli.Contracts;

/// <summary>Whether the caller follows something now, and how many people do.</summary>
public record FollowStateResponse(bool Following, int FollowerCount);

public record FollowedPlaylist(
    Guid PlaylistId, string Name, string Slug, string OwnerUsername, int ItemCount, DateTimeOffset FollowedAt);

public record FollowedUser(string Username, int PublicPlaylistCount, DateTimeOffset FollowedAt);

public record FollowingResponse(
    IReadOnlyList<FollowedPlaylist> Playlists, IReadOnlyList<FollowedUser> Users);

/// <summary>One link that turned up in something the caller follows.</summary>
public record FeedItem(
    Guid ItemId,
    /// <summary>The link behind it — what the reader view is addressed by.</summary>
    Guid LinkId,
    Guid PlaylistId,
    string PlaylistName,
    string PlaylistSlug,
    string OwnerUsername,
    string Url,
    string? Title,
    string Host,
    ContentKind Kind,
    int? WordCount,
    DateTimeOffset AddedAt);

/// <summary>
/// What is new in what the caller follows. <c>NewCount</c> is measured against their own last
/// look rather than a fixed window — that is the only version of "new" that means anything.
/// </summary>
public record FeedResponse(
    IReadOnlyList<FeedItem> Items,
    string? NextCursor,
    int NewCount,
    DateTimeOffset? LastSeenAt);
