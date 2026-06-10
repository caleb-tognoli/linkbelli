namespace Linkbelli.Core.Entities;

/// <summary>
/// Join entity attaching a Source to a Playlist (n-n). New items discovered by
/// the source are appended to every attached playlist.
/// </summary>
public class PlaylistSource : BaseEntity<Guid>
{
    public Guid PlaylistId { get; set; }
    public Guid SourceId { get; set; }

    public Playlist? Playlist { get; set; }
    public Source? Source { get; set; }
}
