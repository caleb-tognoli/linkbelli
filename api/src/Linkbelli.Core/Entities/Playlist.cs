namespace Linkbelli.Core.Entities;

public enum PlaylistVisibility
{
    Private = 0,
    Unlisted = 1,
    Public = 2,
}

public class Playlist : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public PlaylistVisibility Visibility { get; set; } = PlaylistVisibility.Private;

    /// <summary>
    /// The owner's answer on whether this playlist is adult, overriding the automatic reading.
    /// Null means "work it out from the items", which is the default. Detection is a meta tag
    /// and an RTA label — signals a site declares about itself — so it gets false positives, and
    /// without a way to say otherwise one of them hid a playlist from everyone permanently.
    /// </summary>
    public bool? NsfwOverride { get; set; }

    /// <summary>
    /// The link whose image stands for this playlist, chosen by its owner.
    /// </summary>
    /// <remarks>
    /// One of its own items rather than an upload: the picture is already here, already proxied,
    /// and already the right thing — a playlist about a subject is best represented by something
    /// in it. Null falls back to whatever the first item with an image happens to be, which is a
    /// guess rather than a decision.
    /// </remarks>
    public Guid? CoverLinkId { get; set; }

    /// <summary>
    /// The public playlist this was copied from, if it was.
    /// </summary>
    /// <remarks>
    /// Kept so the original's owner can be told how many people took a copy, which says more
    /// about a list than a like count does — a like is a moment's approval, a fork is somebody
    /// deciding to keep it.
    ///
    /// Not a foreign key with a cascade: a fork outlives its original, and deleting the source
    /// should leave every copy standing, just without a parent to point at.
    /// </remarks>
    public Guid? ForkedFromPlaylistId { get; set; }

    /// <summary>
    /// What this was published as before its owner's account was hidden. Null the rest of the
    /// time, which is almost always.
    /// </summary>
    /// <remarks>
    /// Exists for exactly one flow. When an account is suspended or asked to be deleted, every
    /// playlist it owns is set Private — which is the one change that takes effect everywhere at
    /// once: discovery, the sitemap, the profile page, the feeds and every tag facet all filter
    /// on visibility already, so there is no read path left to forget.
    ///
    /// The alternative was a query filter correlating every playlist read against the users
    /// table, or a condition added to twenty-two call sites where missing one is a privacy leak.
    /// This column is the price of neither.
    /// </remarks>
    public PlaylistVisibility? VisibilityBeforeHiding { get; set; }

    public List<PlaylistItem> Items { get; set; } = [];
    public List<PlaylistSource> Sources { get; set; } = [];
    public List<PlaylistTag> Tags { get; set; } = [];
}
