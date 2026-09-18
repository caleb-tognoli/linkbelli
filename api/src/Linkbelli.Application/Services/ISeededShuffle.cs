using System.Linq.Expressions;

namespace Linkbelli.Application.Services;

/// <summary>
/// A random order that stays the same from one page to the next.
/// </summary>
/// <remarks>
/// Shuffle has to be repeatable: a second page dealt from a fresh random order would show links
/// the first page already showed and skip others entirely. Postgres can do that — seed its random
/// number generator, then order by random() — but only on one connection, inside one transaction,
/// with a raw <c>setseed()</c> call. That is the database's business, not the item service's, so it
/// lives behind this the way full-text search lives behind <see cref="IFullTextSearch"/>.
/// </remarks>
public interface ISeededShuffle
{
    /// <summary>
    /// One page of <paramref name="source"/> in the order <paramref name="seed"/> fixes, projected.
    /// </summary>
    /// <param name="seed">Between -1 and 1. The same seed gives the same order.</param>
    Task<List<TResult>> PageAsync<TSource, TResult>(
        IQueryable<TSource> source,
        Expression<Func<TSource, TResult>> project,
        double seed,
        int offset,
        int take,
        CancellationToken ct = default);
}
