namespace Linkbelli.Core.Entities;

/// <summary>
/// Files a playlist into one of the owner's folders. The referenced playlist may be the
/// owner's own (any visibility) or a public playlist owned by someone else that the owner
/// saved for easy access. <see cref="OwnerId"/> is the user doing the filing — it is not
/// necessarily the playlist's owner. A playlist is filed in at most one folder per user.
/// </summary>
public class FolderPlaylist : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public Guid FolderId { get; set; }
    public Guid PlaylistId { get; set; }

    public Folder? Folder { get; set; }
    public Playlist? Playlist { get; set; }
}
