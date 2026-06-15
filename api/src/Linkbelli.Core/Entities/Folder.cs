namespace Linkbelli.Core.Entities;

/// <summary>
/// A private, per-user folder for organizing playlists. Folders nest arbitrarily via
/// <see cref="ParentId"/> (null = a root folder). The folder system is strictly private:
/// it never surfaces on the public/discovery side, and it can hold both the owner's own
/// playlists and public playlists the owner saved for easy access (see <see cref="FolderPlaylist"/>).
/// </summary>
public class Folder : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }

    /// <summary>The containing folder, or null for a root-level folder.</summary>
    public Guid? ParentId { get; set; }

    public Folder? Parent { get; set; }
    public List<Folder> Children { get; set; } = [];
    public List<FolderPlaylist> Playlists { get; set; } = [];
}
