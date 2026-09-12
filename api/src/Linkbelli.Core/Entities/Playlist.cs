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

    public List<PlaylistItem> Items { get; set; } = [];
    public List<PlaylistSource> Sources { get; set; } = [];
    public List<PlaylistTag> Tags { get; set; } = [];
}
