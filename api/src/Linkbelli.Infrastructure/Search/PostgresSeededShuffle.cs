using System.Globalization;
using System.Linq.Expressions;
using Linkbelli.Application.Services;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Infrastructure.Search;

/// <summary>A seeded shuffle, done the way Postgres can do one.</summary>
public sealed class PostgresSeededShuffle(LinkbelliDbContext db) : ISeededShuffle
{
    public async Task<List<TResult>> PageAsync<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<TSource, TResult>> project,
        double seed,
        int offset,
        int take,
        CancellationToken ct = default)
    {
        // setseed() is session state: it has to run on the same connection as the ORDER BY
        // random() it is meant to fix. The transaction is only there to pin that connection —
        // nothing is written, and setseed is not undone by a commit or a rollback.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // A double formatted round-trippable in the invariant culture: digits, a sign, a point
        // and an exponent, nothing that could end the statement.
        var literal = seed.ToString("R", CultureInfo.InvariantCulture);
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SELECT setseed({literal})", ct);
#pragma warning restore EF1002

        var rows = await source
            .OrderBy(_ => EF.Functions.Random())
            .Skip(offset)
            .Take(take)
            .Select(project)
            .ToListAsync(ct);

        await tx.CommitAsync(ct);
        return rows;
    }
}
