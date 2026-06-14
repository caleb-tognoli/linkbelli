using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

/// <summary>Admin moderation: user lookup and host blocklist management.</summary>
public interface IAdminService
{
    Task<IReadOnlyList<AdminUserSummary>> SearchUsersAsync(string? q, int? limit, CancellationToken ct = default);
    Task<IReadOnlyList<AdminHostSummary>> ListHostsAsync(string? q, bool? blocked, int? limit, CancellationToken ct = default);

    /// <summary>Sets a host's blocked flag, creating the host row if it doesn't exist yet.</summary>
    Task<AdminHostSummary> SetHostBlockedAsync(string hostname, bool blocked, CancellationToken ct = default);
}
