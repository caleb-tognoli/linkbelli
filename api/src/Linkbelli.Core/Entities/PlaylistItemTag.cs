namespace Linkbelli.Core.Entities;

/// <summary>
/// Tags one saved link (unique pair), reusing the same globally deduplicated <see cref="Tag"/>
/// rows as playlist tags. Playlist tags describe a list; these describe the thing itself, which
/// is what makes a link findable across the lists it happens to sit in.
/// </summary>
public class PlaylistItemTag : BaseEntity<Guid>
{
    public Guid PlaylistItemId { get; set; }
    public Guid TagId { get; set; }

    public PlaylistItem? PlaylistItem { get; set; }
    public Tag? Tag { get; set; }
}
