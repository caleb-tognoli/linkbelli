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

    /// <summary>
    /// Kept in the sidebar, with a count of what matches right now.
    /// </summary>
    /// <remarks>
    /// A saved search was a question you re-asked by hand from the search page, so "everything
    /// unread from these five sites under ten minutes" could be asked but not <em>had</em> — not
    /// opened from the sidebar, not glanced at, not a thing with a number beside it.
    ///
    /// The full version of this is a playlist whose membership is a query, which brings a pile
    /// of decisions with it: manual ordering, a cover, membership roles, all of which a query
    /// cannot have. Pinning is most of the value for a fraction of that, and a way to find out
    /// whether anybody wants the rest.
    /// </remarks>
    public bool Pinned { get; set; }

    /// <summary>Only links whose page is gone or unreadable.</summary>
    public bool Broken { get; set; }

    /// <summary>"score" for best-rated first; otherwise relevance, or newest.</summary>
    public string? Sort { get; set; }

    /// <summary>Restrict to one kind of thing — "article", "video", "paper"…</summary>
    public string? Kind { get; set; }

    /// <summary>Only what can be read in this many minutes. "Something short" is a saved question.</summary>
    public int? MaxMinutes { get; set; }
}
