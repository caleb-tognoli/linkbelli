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
    /// Stop after this rule matches, so a specific rule can shield an item from a broad one
    /// below it without the broad rule having to enumerate the exceptions.
    /// </summary>
    public bool StopOnMatch { get; set; }

    /// <summary>How many items this rule has acted on, so a rule doing nothing is visible.</summary>
    public int MatchCount { get; set; }

    /// <summary>When it last acted. Null means never, which is usually a rule that doesn't work.</summary>
    public DateTimeOffset? LastMatchedAt { get; set; }
}
