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

    public List<PlaylistItem> Items { get; set; } = [];
    public List<PlaylistSource> Sources { get; set; } = [];
}
