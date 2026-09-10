namespace Linkbelli.Application.Feeds;

/// <summary>One entry in a syndicated playlist: the link itself, as a reader would see it.</summary>
public record FeedEntry(
    /// <summary>Stable, globally unique id for the entry — the playlist item's id.</summary>
    string Id,
    string Title,
    /// <summary>Where the entry points: the link's canonical URL, not a Linkbelli page.</summary>
    string Url,
    string? Summary,
    DateTimeOffset Published,
    string? ImageUrl,
    string? Author);

/// <summary>
/// A playlist rendered as syndication input. Format-independent: the serializers below turn this
/// into RSS, Atom or JSON Feed without touching the database.
/// </summary>
public record FeedDocument(
    string Title,
    string? Description,
    /// <summary>The feed's own address — <c>atom:link rel="self"</c> / JSON Feed <c>feed_url</c>.</summary>
    string SelfUrl,
    /// <summary>The human page this feed accompanies.</summary>
    string HtmlUrl,
    string AuthorName,
    DateTimeOffset Updated,
    IReadOnlyList<FeedEntry> Entries);
