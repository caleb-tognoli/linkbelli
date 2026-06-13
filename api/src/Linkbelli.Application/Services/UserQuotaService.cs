using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class UserQuotaService(IAppDbContext db) : IUserQuotaService
{
    public async Task<UserQuota> GetOrCreateAsync(Guid userId, CancellationToken ct = default)
    {
        var existing = await db.UserQuotas.FirstOrDefaultAsync(q => q.UserId == userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var quota = new UserQuota { UserId = userId };
        db.UserQuotas.Add(quota);
        try
        {
            await db.SaveChangesAsync(ct);
            return quota;
        }
        catch (DbUpdateException)
        {
            db.Entry(quota).State = EntityState.Detached;
            return await db.UserQuotas.FirstAsync(q => q.UserId == userId, ct);
        }
    }

    public Task<int> CountRunsTodayAsync(Guid userId, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        return db.SourceRuns.CountAsync(r => r.Source!.OwnerId == userId && r.CreationTime >= since, ct);
    }

    public async Task<bool> CanRunAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await GetOrCreateAsync(userId, ct);
        return await CountRunsTodayAsync(userId, ct) < quota.MaxRunsPerDay;
    }

    public async Task EnsureCanCreateSourceAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await GetOrCreateAsync(userId, ct);
        var used = await db.Sources.CountAsync(s => s.OwnerId == userId, ct);
        if (used >= quota.MaxSources)
        {
            throw new QuotaExceededException($"Source limit reached ({quota.MaxSources}).");
        }
    }

    public async Task EnsureCanRunAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await GetOrCreateAsync(userId, ct);
        if (await CountRunsTodayAsync(userId, ct) >= quota.MaxRunsPerDay)
        {
            throw new QuotaExceededException($"Daily run limit reached ({quota.MaxRunsPerDay}).");
        }
    }

    public async Task<QuotaResponse> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        var quota = await GetOrCreateAsync(userId, ct);
        var sourcesUsed = await db.Sources.CountAsync(s => s.OwnerId == userId, ct);
        var runsToday = await CountRunsTodayAsync(userId, ct);
        return new QuotaResponse(
            quota.MaxSources, sourcesUsed, quota.MaxRunsPerDay, runsToday, quota.MaxItemsPerRun);
    }

    public async Task<QuotaResponse> SetAsync(
        Guid userId, int maxSources, int maxRunsPerDay, int maxItemsPerRun, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (maxSources < 0) errors["maxSources"] = ["Must be zero or greater."];
        if (maxRunsPerDay < 0) errors["maxRunsPerDay"] = ["Must be zero or greater."];
        if (maxItemsPerRun < 0) errors["maxItemsPerRun"] = ["Must be zero or greater."];
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var quota = await GetOrCreateAsync(userId, ct);
        quota.MaxSources = maxSources;
        quota.MaxRunsPerDay = maxRunsPerDay;
        quota.MaxItemsPerRun = maxItemsPerRun;
        await db.SaveChangesAsync(ct);

        return await GetStatusAsync(userId, ct);
    }
}
