namespace Linkbelli.Core.Entities;

/// <summary>
/// A website (one row per hostname), referenced by every Link on that site.
/// System-owned: created on demand during link persistence, never user-deleted.
/// Accumulates per-site data: favicon/display name (enrichment), moderation
/// blocklist, and later scraper politeness state.
/// </summary>
public class Host : BaseEntity<Guid>
{
    /// <summary>Lowercase, punycode-normalized hostname produced by the canonicalizer.</summary>
    public required string Hostname { get; set; }
    public string? Favicon { get; set; }
    public string? DisplayName { get; set; }
    /// <summary>When true, ingestion refuses links on this host.</summary>
    public bool Blocked { get; set; }
}
