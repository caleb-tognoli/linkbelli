namespace Linkbelli.Core.Entities;

/// <summary>
/// An invitation to join a playlist, opened by somebody who may not have an account yet.
/// </summary>
/// <remarks>
/// Membership resolved an existing username or failed. So to collaborate, the other person had to
/// already have an account on your instance <em>and</em> you had to know their exact username —
/// which, on an instance you have just stood up, nobody does. Paired with registration that can
/// be closed, the operator's only two options were "let the whole internet sign up" and "nobody
/// can ever share anything with me". The two gaps made each other worse.
///
/// Deliberately the same mechanism for both halves of that: an invite is a way into a playlist
/// and, on a closed instance, a way into the instance. One thing to build, one thing to reason
/// about, and one link to send.
/// </remarks>
public class Invite : BaseEntity<Guid>
{
    /// <summary>
    /// The secret in the link, hashed.
    /// </summary>
    /// <remarks>
    /// Stored the way an API key is: the plaintext is shown once, when the link is made, and is
    /// never recoverable afterwards. A table of live invitation tokens is a table of ways into
    /// somebody's playlists.
    /// </remarks>
    public required string TokenHash { get; set; }

    /// <summary>The playlist this lets them into. Null for an invitation to the instance itself.</summary>
    public Guid? PlaylistId { get; set; }

    /// <summary>What they may do there once they arrive.</summary>
    public PlaylistRole Role { get; set; } = PlaylistRole.Viewer;

    /// <summary>Who sent it. Kept so an unexpected name in a member list can be traced back.</summary>
    public Guid InvitedBy { get; set; }

    /// <summary>
    /// Where it was emailed, when it was emailed anywhere.
    /// </summary>
    /// <remarks>
    /// Null when the link was copied out by hand instead, which has to work: mail is optional on
    /// this product by design, and an instance without it still needs a way to invite somebody.
    /// </remarks>
    public string? Email { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When it was used, or null while it has not been. One use only.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Who used it. Null until somebody does.</summary>
    public Guid? AcceptedBy { get; set; }

    public Playlist? Playlist { get; set; }
}
