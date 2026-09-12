using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Services;

/// <summary>
/// Matching and ranking saved text, implemented over whatever the database offers.
/// </summary>
/// <remarks>
/// Behind an interface for the same reason <see cref="Data.IAppDbContext"/> and
/// <c>ISourceScheduler</c> are: full-text search is the most provider-specific thing this
/// application does, and the Application layer should not have to name a Postgres function to
/// ask "does this link mention Dutch railways".
///
/// What it replaces is a chain of <c>LOWER(col) LIKE '%needle%'</c> across seven columns, one of
/// which holds up to 60 000 characters of article text. That could not use an index, and forced
/// a detoast of every candidate row on every keystroke-adjacent search.
/// </remarks>
public interface IFullTextSearch
{
    /// <summary>
    /// Narrows to the items whose link or note matches <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// The query is whatever somebody typed, including an empty string, stray operators and
    /// punctuation. It must never throw on input — a search box is not a parser.
    /// </remarks>
    IQueryable<PlaylistItem> Match(IQueryable<PlaylistItem> items, string query);

    /// <summary>
    /// Best match first, then newest.
    /// </summary>
    /// <remarks>
    /// Relevance is the database's, computed over the same weighted vector the match used, so a
    /// title hit outranks a passing mention in the body. The previous ordering approximated this
    /// with more <c>LIKE</c> expressions in the ORDER BY, which is both slower and worse.
    /// </remarks>
    IQueryable<PlaylistItem> OrderByRelevance(IQueryable<PlaylistItem> items, string query);

    /// <summary>
    /// The passage each of these links matched on, keyed by link id, for the ones whose
    /// article text is what matched.
    /// </summary>
    /// <remarks>
    /// Has to come from the same place the match did. A snippet found by searching the text
    /// for the literal words somebody typed misses every hit that matched through stemming —
    /// "railways" finds an article that only ever says "railway", and then cannot show why.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, string>> SnippetsAsync(
        IReadOnlyCollection<Guid> linkIds, string query, int length, CancellationToken ct = default);
}
