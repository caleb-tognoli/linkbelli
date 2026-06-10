namespace Linkbelli.Core.Entities;

public enum PlaylistItemStatus
{
    Active = 0,
    Pending = 1,
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
    public PlaylistItemStatus Status { get; set; } = PlaylistItemStatus.Active;

    public Playlist? Playlist { get; set; }
    public Link? Link { get; set; }
    public Source? Source { get; set; }
}
