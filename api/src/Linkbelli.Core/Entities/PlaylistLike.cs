namespace Linkbelli.Core.Entities;

/// <summary>
/// One person saying a public playlist is worth something. Deliberately the lightest possible
/// signal: discovery otherwise orders everything by age, which rewards being new rather than
/// being good, and nothing anyone does on a public page is visible to its owner at all.
/// </summary>
/// <remarks>
/// Distinct from saving it to a folder, which is private filing. A like is public and says
/// something to the owner and to everyone else browsing.
/// </remarks>
public class PlaylistLike : BaseEntity<Guid>
{
    public Guid PlaylistId { get; set; }

    /// <summary>Who liked it. Liking needs an account, or the number means nothing.</summary>
    public Guid UserId { get; set; }

    public Playlist? Playlist { get; set; }
}
