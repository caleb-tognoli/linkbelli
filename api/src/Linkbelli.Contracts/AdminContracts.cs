namespace Linkbelli.Contracts;

/// <summary>An admin view of a user (for lookup → quota management).</summary>
public record AdminUserSummary(
    Guid Id, string? Username, string? Email, int PlaylistCount, int SourceCount, bool ShowNsfw);

/// <summary>An admin view of a host (moderation blocklist).</summary>
public record AdminHostSummary(Guid Id, string Hostname, bool Blocked, int LinkCount);

/// <summary>Block or unblock a host by name (created if not yet seen).</summary>
public record SetHostBlockedRequest(string Hostname, bool Blocked);
