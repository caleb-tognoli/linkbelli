namespace Linkbelli.Core.Entities;

/// <summary>How the last attempt to fetch a link's page went.</summary>
public enum EnrichmentStatus
{
    /// <summary>Never successfully fetched yet.</summary>
    Pending = 0,

    /// <summary>Fetched and read.</summary>
    Succeeded = 1,

    /// <summary>The fetch failed for a reason that may not last (blocked, non-HTML, a 4xx).</summary>
    Failed = 2,

    /// <summary>The page is gone — a 404 or 410. The link still exists; the thing it pointed at doesn't.</summary>
    Broken = 3,
}

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
    /// <summary>
    /// When we last finished trying to enrich this link, successfully or not. Reads gate on this
    /// being set, so an item appears once we have stopped waiting on it either way.
    /// </summary>
    public DateTimeOffset? EnrichedAt { get; set; }

    /// <summary>How the last attempt went.</summary>
    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;

    /// <summary>Why the last attempt failed, in a form worth showing someone.</summary>
    public string? EnrichmentError { get; set; }

    /// <summary>
    /// When the page was last fetched. Distinct from <see cref="EnrichedAt"/>, which is only
    /// stamped the first time: this moves on every re-check.
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Consecutive failed checks, used to back off rather than retry on a fixed beat.</summary>
    public int FailureCount { get; set; }
    /// <summary>Adult content. Set automatically only (content rating tags or ingestion from an NSFW source).</summary>
    public bool Nsfw { get; set; }

    public Host? Host { get; set; }
}
