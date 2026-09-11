namespace Linkbelli.Core.Entities;

/// <summary>
/// How one user likes to look at one playlist: sort, filters, and what the rows show. Previously
/// this lived in a 50-entry browser cookie that was sent on every single request, evicted the
/// oldest playlist once full, and never followed anyone to another device.
/// </summary>
public class PlaylistPreference : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public Guid PlaylistId { get; set; }

    /// <summary>Server sort name ("position", "date-desc", "shuffle", "score-desc"…).</summary>
    public string? Sort { get; set; }

    /// <summary>Source filter: a source id, "manual", or null for all.</summary>
    public string? Source { get; set; }

    /// <summary>"All", "Watched" or "Unwatched"; null falls back to the view's own default.</summary>
    public string? Status { get; set; }

    public bool ShowUrls { get; set; }

    public bool ShowThumbnails { get; set; } = true;

    public Playlist? Playlist { get; set; }
}
