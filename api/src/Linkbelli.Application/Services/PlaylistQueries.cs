using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>Filters more than one playlist service needs.</summary>
public static class PlaylistQueries
{
    /// <summary>An AND tag filter: the playlist must carry every (normalized) tag.</summary>
    public static IQueryable<Playlist> WithEveryTag(this IQueryable<Playlist> query, string[]? tags)
    {
        foreach (var raw in tags ?? [])
        {
            var name = TagNormalizer.NormalizeOne(raw);
            if (name.Length > 0)
            {
                query = query.Where(p => p.Tags.Any(pt => pt.Tag!.Name == name));
            }
        }

        return query;
    }
}

/// <summary>Tags with how often they are used, for autocomplete and tag clouds.</summary>
public static class TagCounts
{
    /// <summary>The most tags any one of these lists returns.</summary>
    public const int MaxResults = 200;

    public static async Task<IReadOnlyList<TagSummary>> ListAsync(
        IQueryable<PlaylistTag> scope, string? q, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var prefix = TagNormalizer.NormalizeOne(q);
            scope = scope.Where(pt => pt.Tag!.Name.StartsWith(prefix));
        }

        var rows = await scope
            .GroupBy(pt => pt.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(MaxResults)
            .ToListAsync(ct);

        return rows.Select(x => new TagSummary(x.Name, x.Count)).ToList();
    }
}
