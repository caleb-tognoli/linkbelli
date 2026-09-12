using System.Collections.ObjectModel;
using Linkbelli.Application.Services;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Linkbelli.Infrastructure.Search;

/// <summary>
/// Full-text search over Postgres, against the stored vector on each link.
/// </summary>
/// <remarks>
/// The vector is a generated column (see <see cref="LinkbelliDbContext"/>), so the database
/// maintains it on write and it can never drift from the row. It is weighted, so a title match
/// outranks a site name, which outranks a passing mention in the body.
///
/// Not everything lives in the vector. The URL and hostname are matched literally, because
/// "bbc.co.uk/news" is not English and a stemmer makes nonsense of it; notes likewise. Both are
/// short columns, so that scan costs almost nothing now that the 60 000-character article body
/// is no longer part of it.
/// </remarks>
public sealed class PostgresFullTextSearch(LinkbelliDbContext db) : IFullTextSearch
{
    /// <summary>The name the vector is mapped under. A shadow property: no entity carries it.</summary>
    public const string VectorProperty = "SearchVector";

    /// <summary>
    /// Must match the configuration the generated column was built with, or a query asks a
    /// differently-stemmed question of the same index and quietly misses things.
    /// </summary>
    public const string Configuration = "english";

    public IQueryable<PlaylistItem> Match(IQueryable<PlaylistItem> items, string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return items;
        }

        var literal = $"%{Escape(text)}%";

        return items.Where(i =>
            EF.Property<NpgsqlTsVector>(i.Link!, VectorProperty)
                .Matches(EF.Functions.WebSearchToTsQuery(Configuration, text))
            || EF.Functions.ILike(i.Link!.CanonicalUrl, literal, "\\")
            || EF.Functions.ILike(i.Link!.Host!.Hostname, literal, "\\")
            || (i.Note != null && EF.Functions.ILike(i.Note, literal, "\\")));
    }

    public IQueryable<PlaylistItem> OrderByRelevance(IQueryable<PlaylistItem> items, string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return items.OrderByDescending(i => i.CreationTime).ThenByDescending(i => i.Id);
        }

        // Cover density rather than plain rank: it accounts for how close the matched words are
        // to each other, which is what separates an article about a subject from one that
        // happens to use both words in unrelated paragraphs.
        return items
            .OrderByDescending(i => EF.Property<NpgsqlTsVector>(i.Link!, VectorProperty)
                .RankCoverDensity(EF.Functions.WebSearchToTsQuery(Configuration, text)))
            .ThenByDescending(i => i.CreationTime)
            .ThenByDescending(i => i.Id);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> SnippetsAsync(
        IReadOnlyCollection<Guid> linkIds, string query, int length, CancellationToken ct = default)
    {
        var text = query.Trim();
        if (linkIds.Count == 0 || text.Length == 0)
        {
            return ReadOnlyDictionary<Guid, string>.Empty;
        }

        var ids = linkIds.ToArray();

        // Words rather than characters, because that is what ts_headline counts. MaxFragments=0
        // asks for one window around the best match rather than several stitched together.
        //
        // The markers have to be quoted empty strings. Written bare as `StartSel=, StopSel=,`
        // Postgres reads the comma as the value, and every matched word comes back wrapped in
        // commas — which is what it did.
        var options = $"StartSel=\"\", StopSel=\"\", MaxWords={Math.Max(8, length / 8)}, "
            + $"MinWords={Math.Max(4, length / 20)}, MaxFragments=0";

        var rows = await db.Database
            .SqlQuery<SnippetRow>(
                $"""
                 SELECT l."Id" AS "LinkId",
                        ts_headline(
                            {Configuration}::regconfig,
                            l."Content",
                            websearch_to_tsquery({Configuration}::regconfig, {text}),
                            {options}) AS "Text"
                 FROM "Links" AS l
                 WHERE l."Id" = ANY({ids}) AND l."Content" IS NOT NULL
                 """)
            .ToListAsync(ct);

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .ToDictionary(r => r.LinkId, r => r.Text.Trim());
    }

    private sealed record SnippetRow(Guid LinkId, string Text);

    /// <summary>
    /// Makes a typed string safe as a LIKE pattern.
    /// </summary>
    /// <remarks>
    /// Somebody searching for "100%" or "some_file" means those characters literally, and
    /// without this they are wildcards — so the search quietly returns far too much rather than
    /// what was asked for. The escape character is named explicitly at each call site, because
    /// Postgres only honours a backslash here if it is told to.
    /// </remarks>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
