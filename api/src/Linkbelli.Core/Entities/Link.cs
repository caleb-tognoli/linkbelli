namespace Linkbelli.Core.Entities;

/// <summary>
/// A globally deduplicated link. Shared across playlists; identified by the
/// SHA-256 hash of its canonicalized URL.
/// </summary>
public class Link : BaseEntity<Guid>
{
    public required string CanonicalUrl { get; set; }
    public required string UrlHash { get; set; }
    /// <summary>Derived from CanonicalUrl by the canonicalizer; recomputable.</summary>
    public Guid HostId { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? SiteName { get; set; }
    /// <summary>Raw scraped/OpenGraph metadata (jsonb).</summary>
    public string? Metadata { get; set; }
    public DateTimeOffset? EnrichedAt { get; set; }
    /// <summary>Adult content. Set automatically only (content rating tags or ingestion from an NSFW source).</summary>
    public bool Nsfw { get; set; }

    public Host? Host { get; set; }
}
