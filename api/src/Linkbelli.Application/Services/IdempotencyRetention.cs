using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Services;

/// <summary>Forgets idempotency keys once they are old enough that nothing will retry them.</summary>
public interface IIdempotencyRetention
{
    Task<int> PurgeAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class IdempotencyRetention(
    IAppDbContext db, ILogger<IdempotencyRetention> logger) : IIdempotencyRetention
{
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-IdempotencyRecord.RetentionHours);

        // Deleted with a statement, like everywhere else these rows are touched: they are
        // bookkeeping, not content, and a soft-deleted key would still be found by the lookup.
        var removed = await db.IdempotencyRecords
            .Where(r => r.CreationTime < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0)
        {
            logger.LogInformation("Forgot {Count} idempotency keys older than {Cutoff}.", removed, cutoff);
        }

        return removed;
    }
}
