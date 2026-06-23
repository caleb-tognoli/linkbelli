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
    /// <summary>Source-provided metadata (title, thumbnail, author, etc.). Null when added manually.</summary>
    public Dictionary<string, string>? Metadata { get; set; }
    /// <summary>Owner-assigned score (0–100). Null means unrated.</summary>
    public int? Score { get; set; }

    public Playlist? Playlist { get; set; }
    public Link? Link { get; set; }
    public Source? Source { get; set; }
}
