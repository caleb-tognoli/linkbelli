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
