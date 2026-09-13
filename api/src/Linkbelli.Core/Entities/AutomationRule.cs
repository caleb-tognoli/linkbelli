using Linkbelli.Core.Content;

namespace Linkbelli.Core.Entities;

/// <summary>
/// One "when this, do that" over a person's own collection. Everything arriving from a source
/// lands wherever the source was pointed and stays there: filing it, tagging it, or getting it
/// out of the way is a decision they have to make again for every single item.
/// </summary>
public class AutomationRule : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }

    /// <summary>Off keeps the rule and its history without it acting. Deleting is for wrong rules.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Rules run in this order, lowest first. Order matters because a rule can move an item out
    /// from under the ones after it.
    /// </summary>
    public int Position { get; set; }

    // --- When ---

    /// <summary>Only items landing in this playlist. Null applies it to everything.</summary>
    public Guid? PlaylistId { get; set; }

    /// <summary>Exact hostname, already lowercased.</summary>
    public string? Host { get; set; }

    /// <summary>Regular expression over the title, case-insensitive.</summary>
    public string? TitlePattern { get; set; }

    public string? UrlPattern { get; set; }

    /// <summary>Only this kind of thing — a video, a paper. Null matches any.</summary>
    public ContentKind? Kind { get; set; }

    /// <summary>
    /// Only what can be read in this many minutes or fewer. Null matches any length.
    /// </summary>
    /// <remarks>
    /// Search could ask "articles under five minutes" from the day it shipped and rules could
    /// not, off the same stored word count. The Rules page's own example copy suggests "send
    /// anything over twenty minutes to a later list", which is a condition the engine could not
    /// express — hence both ends of the range, not just this one.
    ///
    /// Only known after enrichment, like <see cref="Kind"/>. The sweep already waits for that, so
    /// an unenriched item is deferred rather than quietly failing to match.
    /// </remarks>
    public int? MaxMinutes { get; set; }

    /// <summary>Only what takes at least this many minutes. Null matches any length.</summary>
    public int? MinMinutes { get; set; }

    /// <summary>
    /// True for only the links whose page is gone or unreadable; false for only the ones that
    /// are fine; null for either.
    /// </summary>
    /// <remarks>
    /// The link rot in a collection, which search could already ask about. "Tag anything broken
    /// and move it out of my queue" is the rule people actually want.
    /// </remarks>
    public bool? Broken { get; set; }

    /// <summary>Only items a particular source brought in. Null matches any origin.</summary>
    public Guid? SourceId { get; set; }

    // --- Then ---

    /// <summary>Tags to put on the item. Normalized when the rule is saved, not when it runs.</summary>
    public string[] AddTags { get; set; } = [];

    /// <summary>Move the item to this playlist. Mutually exclusive with <see cref="CopyToPlaylistId"/>.</summary>
    public Guid? MoveToPlaylistId { get; set; }

    /// <summary>Leave the item where it is and put a second one in this playlist.</summary>
    public Guid? CopyToPlaylistId { get; set; }

    /// <summary>Mark it watched on arrival — for things kept as a record rather than a queue.</summary>
    public bool MarkWatched { get; set; }

    /// <summary>Move it straight to the trash. A recoverable "not this, thanks".</summary>
    public bool Trash { get; set; }

    /// <summary>
    /// Give it this score on arrival. Null leaves the score alone.
    /// </summary>
    /// <remarks>
    /// The queue sorts on score, so this is how a rule says "this source is worth my time" or
    /// "this one is background reading" without anybody rating a single item by hand.
    /// </remarks>
    public int? SetScore { get; set; }

    /// <summary>
    /// Ask the Internet Archive for a public snapshot of the page.
    /// </summary>
    /// <remarks>
    /// Per rule rather than per account, which is the useful grain: somebody may well want a
    /// permanent copy of everything from one site and of nothing else. The account-wide setting
    /// still applies on top; this only ever adds.
    /// </remarks>
    public bool Archive { get; set; }

    /// <summary>
    /// Stop after this rule matches, so a specific rule can shield an item from a broad one
    /// below it without the broad rule having to enumerate the exceptions.
    /// </summary>
    public bool StopOnMatch { get; set; }

    /// <summary>How many items this rule has acted on, so a rule doing nothing is visible.</summary>
    public int MatchCount { get; set; }

    /// <summary>When it last acted. Null means never, which is usually a rule that doesn't work.</summary>
    public DateTimeOffset? LastMatchedAt { get; set; }
}
