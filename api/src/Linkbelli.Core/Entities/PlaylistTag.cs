namespace Linkbelli.Core.Entities;

/// <summary>Join row tagging a playlist with a tag (unique pair).</summary>
public class PlaylistTag : BaseEntity<Guid>
{
    public Guid PlaylistId { get; set; }
    public Guid TagId { get; set; }

    public Playlist? Playlist { get; set; }
    public Tag? Tag { get; set; }
}
