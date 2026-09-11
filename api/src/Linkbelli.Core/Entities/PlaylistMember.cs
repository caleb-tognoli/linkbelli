namespace Linkbelli.Core.Entities;

/// <summary>What someone invited to a playlist may do in it.</summary>
public enum PlaylistRole
{
    /// <summary>Can read it, however it is set. Nothing else.</summary>
    Viewer = 0,

    /// <summary>Can also add links — the shape of "help me collect things".</summary>
    Contributor = 1,

    /// <summary>Can also reorder, edit and remove. Everything but giving it away.</summary>
    Editor = 2,
}

/// <summary>
/// Someone other than the owner with access to a playlist.
/// </summary>
/// <remarks>
/// Sharing was all-or-nothing public: showing one list to one person meant publishing it to
/// everyone. Collaborating on one meant handing over an account.
/// </remarks>
public class PlaylistMember : BaseEntity<Guid>
{
    public Guid PlaylistId { get; set; }

    public Guid UserId { get; set; }

    public PlaylistRole Role { get; set; } = PlaylistRole.Viewer;

    /// <summary>Who added them. Kept so the owner can see where an unexpected name came from.</summary>
    public Guid InvitedBy { get; set; }

    public Playlist? Playlist { get; set; }
}
