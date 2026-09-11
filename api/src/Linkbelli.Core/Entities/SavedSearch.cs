namespace Linkbelli.Core.Entities;

/// <summary>
/// A search someone wants to come back to — "unread, from this site, rated above 70". The
/// membership is whatever matches right now, so it keeps up with the collection on its own.
///
/// Deliberately its own thing rather than a flag on <see cref="Playlist"/>. A playlist can be
/// reordered, added to, syndicated and exported; a query-defined list can do none of those, and
/// making one polymorphic would put that branch into every read of every playlist.
/// </summary>
public class SavedSearch : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }

    /// <summary>Free text, matched the same way the search endpoint matches it.</summary>
    public string? Query { get; set; }

    /// <summary>Restrict to one site.</summary>
    public string? Host { get; set; }

    /// <summary>Playlist tags that must all be present.</summary>
    public string[] Tags { get; set; } = [];

    /// <summary>Tags on the link itself that must all be present.</summary>
    public string[] ItemTags { get; set; } = [];

    /// <summary>"watched" or "unwatched".</summary>
    public string? Status { get; set; }

    public int? MinScore { get; set; }

    /// <summary>Only links whose page is gone or unreadable.</summary>
    public bool Broken { get; set; }

    /// <summary>"score" for best-rated first; otherwise relevance, or newest.</summary>
    public string? Sort { get; set; }
}
