namespace Linkbelli.Core.Entities;

public enum PlaylistItemStatus
{
    Added = 0,
    Watched = 1,
}

public class PlaylistItem : BaseEntity<Guid>
{
    /// <summary>Gap used between consecutive positions so reorders rarely renumber.</summary>
    public const int PositionGap = 1024;

    public Guid PlaylistId { get; set; }
    public Guid LinkId { get; set; }
    public long Position { get; set; }
    public string? Note { get; set; }
    /// <summary>Null when added manually; otherwise the source that discovered it.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>
    /// Who put this here.
    /// </summary>
    /// <remarks>
    /// A playlist can be shared with editors and contributors, and nothing recorded which of them
    /// added what — so on a list three people contribute to, "who saved this?" had no answer, and
    /// neither did "show me only mine". SourceId already answered the machine version of the same
    /// question, which is what made the absence of this one look like an oversight.
    ///
    /// Nullable, and null for everything that existed before this was tracked: inventing an
    /// answer would be worse than admitting there isn't one. Also null for items a source
    /// created, where SourceId is the honest answer instead.
    /// </remarks>
    public Guid? AddedByUserId { get; set; }
    public PlaylistItemStatus Status { get; set; } = PlaylistItemStatus.Added;

    /// <summary>
    /// When <see cref="Status"/> last changed. Null while an item has never moved off Added —
    /// and on items that changed before this was tracked, which is why it is nullable rather
    /// than falling back to the creation time and inventing history.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; set; }
    /// <summary>Source-provided metadata (title, thumbnail, author, etc.). Null when added manually.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
    /// <summary>Owner-assigned score (0–100). Null means unrated.</summary>
    public int? Score { get; set; }

    /// <summary>
    /// How far through the article this is, from 0 to 1. Null until it has been opened.
    /// </summary>
    /// <remarks>
    /// Status has two values, so a twenty-two-minute piece read half of on the train was
    /// indistinguishable from one never opened: "Up next" kept offering it from the top, and the
    /// only way to clear it was to lie by marking it watched.
    ///
    /// Three states are derived from this rather than added to the enum — untouched, in progress,
    /// finished — so nothing that already reads Status has to change.
    /// </remarks>
    public double? ReadProgress { get; set; }

    /// <summary>When the reader was last open on this. Null until it has been.</summary>
    public DateTimeOffset? LastReadAt { get; set; }

    /// <summary>
    /// Put aside until this moment. Null when it is not.
    /// </summary>
    /// <remarks>
    /// Every other timestamp on this row looks backwards. Ordering the queue by age stops new
    /// arrivals burying old ones and does nothing about the item offered forty times and skipped
    /// forty times, which is the actual way a backlog becomes permanent. "Not now" was the
    /// missing verb.
    ///
    /// The same column pointed the other way is "remind me": a reference you will want again in
    /// six months is snoozed rather than left in a list you have finished with.
    /// </remarks>
    public DateTimeOffset? SnoozedUntil { get; set; }

    /// <summary>
    /// How many times this has been put aside.
    /// </summary>
    /// <remarks>
    /// An item passed over repeatedly is a signal. Offering to get rid of it is kinder than
    /// silently re-offering it, and than letting somebody feel guilty about a list they will
    /// never read.
    /// </remarks>
    public int SnoozeCount { get; set; }

    /// <summary>
    /// When the owner's automation rules were run over this item. Null means they haven't been —
    /// which is what the sweep looks for, and what stops a rule acting on the same item twice.
    /// </summary>
    public DateTimeOffset? AutomationAppliedAt { get; set; }

    /// <summary>
    /// The opaque token this item is shared under, or null when it isn't shared. Sharing one
    /// link otherwise meant making a whole playlist public, or sending a bare URL and losing the
    /// note that was the reason for sending it.
    /// </summary>
    /// <remarks>
    /// A token rather than the item id: the id appears in the owner's own URLs, and a share link
    /// gets forwarded — it must not double as a key to anything else, or hint at what else is in
    /// the playlist it came from.
    /// </remarks>
    public string? ShareToken { get; set; }

    /// <summary>When it was first shared. Revoking clears the token, not this.</summary>
    public DateTimeOffset? SharedAt { get; set; }

    /// <summary>Tags on the link itself, as opposed to on the playlist holding it.</summary>
    public List<PlaylistItemTag> Tags { get; set; } = [];

    public Playlist? Playlist { get; set; }
    public Link? Link { get; set; }
    public Source? Source { get; set; }
}
