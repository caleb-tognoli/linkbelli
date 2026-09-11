namespace Linkbelli.Core.Entities;

/// <summary>
/// Someone keeping up with a playlist, or with everything a person publishes.
/// </summary>
/// <remarks>
/// Saving a public playlist to a folder is filing a copy of it: it says where you put it, not
/// that you want to hear about it again. Following is the next primitive up — it is what makes
/// "anything new since I last looked" a question the app can answer.
/// </remarks>
public class Follow : BaseEntity<Guid>
{
    /// <summary>Who is following.</summary>
    public Guid FollowerId { get; set; }

    /// <summary>The playlist being followed, when it is a playlist.</summary>
    public Guid? PlaylistId { get; set; }

    /// <summary>
    /// The person being followed, when it is a person — which means everything they publish,
    /// including playlists they haven't made yet.
    /// </summary>
    public Guid? FollowedUserId { get; set; }

    public Playlist? Playlist { get; set; }
}
