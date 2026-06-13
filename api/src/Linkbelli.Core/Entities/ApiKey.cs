namespace Linkbelli.Core.Entities;

/// <summary>
/// A long-lived API token for programmatic access, owned by a user. Only the
/// SHA-256 hash of the secret is stored; the secret is shown once at creation.
/// Revocation is a soft delete (DeletionTime).
/// </summary>
public class ApiKey : BaseEntity<Guid>
{
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    /// <summary>Public, indexed identifier embedded in the token; safe to display.</summary>
    public required string Prefix { get; set; }
    /// <summary>SHA-256 (hex) of the token secret. The secret itself is never stored.</summary>
    public required string Hash { get; set; }
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
