namespace Linkbelli.Application.Feeds;

/// <summary>
/// Serves a non-private playlist as a syndication feed. Linkbelli reads RSS, Atom and JSON feeds
/// from day one; this is the other direction, so a playlist can be followed from any reader —
/// including by another Linkbelli source.
/// </summary>
public interface IPlaylistFeedService
{
    /// <summary>Most recent items a feed carries. Readers don't want the whole archive.</summary>
    const int MaxEntries = 50;

    /// <summary>
    /// Builds the feed for a public or unlisted playlist. Private playlists (and playlists the
    /// viewer's NSFW preference hides) throw NotFoundException, exactly like the HTML read.
    /// </summary>
    /// <param name="selfUrl">The feed's own address, for rel="self".</param>
    /// <param name="htmlUrl">The human page this feed accompanies.</param>
    Task<FeedDocument> BuildAsync(
        string username, string slug, string selfUrl, string htmlUrl, CancellationToken ct = default);
}
