using Linkbelli.Contracts;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Services;

/// <summary>Per-user quota lookup and enforcement (limits stored in the DB, defaults on first use).</summary>
public interface IUserQuotaService
{
    Task<UserQuota> GetOrCreateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Number of executed runs across the user's sources in the trailing 24 hours.</summary>
    Task<int> CountRunsTodayAsync(Guid userId, CancellationToken ct = default);

    /// <summary>True if the user is under their daily run limit (non-throwing, for background runs).</summary>
    Task<bool> CanRunAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Throws QuotaExceededException if the user is at their source limit.</summary>
    Task EnsureCanCreateSourceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Throws QuotaExceededException if the user is at their daily run limit.</summary>
    Task EnsureCanRunAsync(Guid userId, CancellationToken ct = default);

    Task<QuotaResponse> GetStatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin: overrides a user's limits (creating the quota row if needed).</summary>
    Task<QuotaResponse> SetAsync(Guid userId, int maxSources, int maxRunsPerDay, int maxItemsPerRun, CancellationToken ct = default);
}
