using System.Linq.Expressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>Finding public playlists: browsing, ranking, "more like this", trending tags, the sitemap.</summary>
public interface IDiscoveryService
{
    /// <summary>
    /// Discovery of public playlists, filtered by name and tag and the viewer's NSFW preference,
    /// in a given order: "active", "liked", "largest", or newest when nothing is asked for.
    /// </summary>
    Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(
        string? q, string[]? tags, string? sort, int? limit, string? cursor, Guid? viewerId,
        CancellationToken ct = default);

    /// <summary>Every public playlist's address and date, for a sitemap.</summary>
    Task<PagedResult<SitemapEntry>> ListForSitemapAsync(
        int? limit, string? cursor, CancellationToken ct = default);

    /// <summary>Public playlists like this one — sharing its tags, or holding the same links.</summary>
    Task<IReadOnlyList<PublicPlaylistSummary>> ListSimilarAsync(
        string username, string slug, int? limit, Guid? viewerId, CancellationToken ct = default);

    /// <summary>Tags on public playlists that have seen activity lately.</summary>
    Task<IReadOnlyList<TagSummary>> ListTrendingTagsAsync(int? days, CancellationToken ct = default);

    /// <summary>Tags used across public playlists, with counts (global discovery tag cloud).</summary>
    Task<IReadOnlyList<TagSummary>> ListPublicTagsAsync(string? q, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// The queries that run across everybody's public playlists at once, which is what makes them
/// the expensive ones — and why they sit together, where a change to one is read next to the
/// others and the measured costs written beside them.
/// </remarks>
public class DiscoveryService(IAppDbContext db, IUserPreferenceService prefs) : IDiscoveryService
{
    public async Task<PagedResult<PublicPlaylistSummary>> DiscoverPublicAsync(
        string? q, string[]? tags, string? sort, int? limit, string? cursor, Guid? viewerId,
        CancellationToken ct = default)
    {
        var take = Paging.Take(limit);
        var ranking = sort?.Trim().ToLowerInvariant();

        var query = db.Playlists.Where(p => p.Visibility == PlaylistVisibility.Public);
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Provider-agnostic case-insensitive contains (translates to LOWER(name) LIKE …).
            var needle = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(needle));
        }

        query = query.WithEveryTag(tags);

        query = query.VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));

        // Two of the four orderings run off a value the cursor can name. The other two rank by a
        // correlated count — likes, items — which is not a column, so there is nothing to compare
        // the next page against and they stay on offsets. Said here rather than discovered later:
        // "cursor" is opaque to clients, and only one of these two kinds is actually stable.
        if (ranking is "liked" or "largest")
        {
            var offset = Cursor.DecodeOffset(cursor);

            // Ordered first, projected second: the owner's name comes from a subquery rather than
            // a join so the ordering stays expressed over the playlist itself, which is the only
            // form EF can translate.
            var ranked = await ByCount(query, ranking)
                .Select(Summarize())
                .Skip(offset).Take(take + 1)
                .ToListAsync(ct);

            string? more = null;
            if (ranked.Count > take)
            {
                ranked.RemoveAt(take);
                more = Cursor.Encode((offset + take).ToString());
            }

            return new PagedResult<PublicPlaylistSummary>([.. ranked.Select(r => r.Row)], more);
        }

        var after = Cursor.DecodeTimeKey(cursor);

        // "active" is ordered by the newest thing in the list; everything else by when the list
        // itself appeared. Both fall back to the id, which is what makes the position total —
        // several playlists created in the same tick is a normal outcome of an import.
        var byActivity = ranking == "active";

        var rows = await query
            .Select(p => new
            {
                Playlist = p,
                Key = byActivity
                    ? p.Items.Max(i => (DateTimeOffset?)i.CreationTime) ?? p.CreationTime
                    : p.CreationTime,
            })
            .Where(x => after == null
                || x.Key < after.Value.At
                || (x.Key == after.Value.At && x.Playlist.Id.CompareTo(after.Value.Id) < 0))
            .OrderByDescending(x => x.Key).ThenByDescending(x => x.Playlist.Id)
            .Take(take + 1)
            .Select(x => new KeyedRow<PublicPlaylistSummary>(
                x.Key,
                x.Playlist.Id,
                new PublicPlaylistSummary(
                    db.Users.Where(u => u.Id == x.Playlist.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                    x.Playlist.Slug, x.Playlist.Name, x.Playlist.Description,
                    x.Playlist.Items.Count(i => i.Link!.EnrichedAt != null), x.Playlist.CreationTime,
                    x.Playlist.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                    x.Playlist.NsfwOverride != null
                        ? x.Playlist.NsfwOverride.Value
                        : x.Playlist.Items.Any(i => i.Link!.Nsfw),
                    db.PlaylistLikes.Count(l => l.PlaylistId == x.Playlist.Id),
                    x.Playlist.Items.Max(i => (DateTimeOffset?)i.CreationTime))))
            .ToListAsync(ct);

        return rows.ToPage(take);
    }

    /// <summary>
    /// How many sitemap rows one request will hand over.
    /// </summary>
    /// <remarks>
    /// Far above the hundred every other listing allows, because a row here is three short fields
    /// with no subqueries behind it. The point of the endpoint is that a crawler fetch costs one
    /// round trip instead of fifty.
    /// </remarks>
    public const int SitemapPageSize = 5_000;

    public async Task<PagedResult<SitemapEntry>> ListForSitemapAsync(
        int? limit, string? cursor, CancellationToken ct = default)
    {
        var take = Paging.Take(limit, max: SitemapPageSize, fallback: SitemapPageSize);
        var after = Cursor.DecodeTimeKey(cursor);

        // Public only. Unlisted is share-by-link and deliberately unfindable, so handing it to a
        // crawler would undo the only thing the setting does.
        var rows = await db.Playlists
            .Where(p => p.Visibility == PlaylistVisibility.Public)
            .Where(p => after == null
                || p.CreationTime < after.Value.At
                || (p.CreationTime == after.Value.At && p.Id.CompareTo(after.Value.Id) < 0))
            .OrderByDescending(p => p.CreationTime).ThenByDescending(p => p.Id)
            .Take(take + 1)
            .Select(p => new KeyedRow<SitemapEntry>(
                p.CreationTime,
                p.Id,
                new SitemapEntry(
                    db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                    p.Slug,
                    // The newest thing in the list, or the list itself when it is empty: what
                    // <lastmod> is supposed to mean, and the only field here worth a subquery.
                    p.Items.Max(i => (DateTimeOffset?)i.CreationTime) ?? p.CreationTime)))
            .ToListAsync(ct);

        return rows.ToPage(take);
    }

    /// <summary>
    /// One public playlist as a listing shows it, with its id and date still attached.
    /// </summary>
    /// <remarks>
    /// The summary identifies a playlist by owner and slug, which is right for a public URL and
    /// useless for matching a row back to something computed about it. Both callers need that —
    /// one to build a cursor, the other to re-apply a similarity score — so the key rides along
    /// and is dropped at the boundary.
    /// </remarks>
    private Expression<Func<Playlist, KeyedRow<PublicPlaylistSummary>>> Summarize() =>
        p => new KeyedRow<PublicPlaylistSummary>(
            p.CreationTime,
            p.Id,
            new PublicPlaylistSummary(
                db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                p.Slug, p.Name, p.Description,
                p.Items.Count(i => i.Link!.EnrichedAt != null), p.CreationTime,
                p.Tags.Select(pt => pt.Tag!.Name).ToArray(),
                p.NsfwOverride != null ? p.NsfwOverride.Value : p.Items.Any(i => i.Link!.Nsfw),
                db.PlaylistLikes.Count(l => l.PlaylistId == p.Id),
                p.Items.Max(i => (DateTimeOffset?)i.CreationTime)));

    /// <summary>
    /// How much of a playlist's link set is compared when looking for similar ones.
    /// </summary>
    /// <remarks>
    /// Every id used to go into the comparison, as one array parameter evaluated against every
    /// item of every public playlist. What a list is about is answered just as well by its recent
    /// few hundred links, and the cap is what stops one enormous playlist making this expensive
    /// for everybody who opens it.
    /// </remarks>
    public const int SimilarityLinkSample = 500;

    /// <summary>
    /// The two discovery orderings that rank by a count rather than by a date.
    /// </summary>
    /// <remarks>
    /// Ordering everything by age rewards being new rather than being good, and a list posted
    /// last year that people keep coming back to was unfindable. These two are the answer to
    /// that, and they are also the two that cannot page by cursor: a correlated count is not a
    /// column, so there is no value for the next page to resume after.
    ///
    /// The date-ordered pair — newest, and "active" — are handled in
    /// <see cref="DiscoverPublicAsync"/> itself, where the key can travel in the cursor.
    ///
    /// Expressed over the entities rather than over the projected summary: EF cannot translate an
    /// OrderBy that reaches into a type the query has just constructed.
    /// </remarks>
    private IOrderedQueryable<Playlist> ByCount(IQueryable<Playlist> playlists, string sort) =>
        sort == "liked"
            ? playlists
                .OrderByDescending(p => db.PlaylistLikes.Count(l => l.PlaylistId == p.Id))
                .ThenByDescending(p => p.CreationTime)
                .ThenByDescending(p => p.Id)
            : playlists
                .OrderByDescending(p => p.Items.Count(i => i.Link!.EnrichedAt != null))
                .ThenByDescending(p => p.CreationTime)
                .ThenByDescending(p => p.Id);

    /// <summary>
    /// Public playlists that look like this one: sharing its tags, or holding the same links.
    /// </summary>
    /// <remarks>
    /// Shared links are the stronger signal and are weighted accordingly — two lists holding the
    /// same twenty pages are about the same thing whatever anyone tagged them.
    /// </remarks>
    public async Task<IReadOnlyList<PublicPlaylistSummary>> ListSimilarAsync(
        string username, string slug, int? limit, Guid? viewerId, CancellationToken ct = default)
    {
        var take = Paging.Take(limit, max: 24, fallback: 6);
        var normalized = username.ToUpperInvariant();

        var subject = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new
            {
                p.Id,
                Tags = p.Tags.Select(pt => pt.TagId).ToList(),
                // Newest first and capped. The whole set went into the comparison before, so a
                // five-thousand-link playlist sent five thousand ids as one array parameter; what
                // a list is about is answered just as well by its recent half, and the cap is what
                // stops one enormous playlist making this expensive for everybody who opens it.
                Links = p.Items.OrderByDescending(i => i.CreationTime)
                    .Select(i => i.LinkId)
                    .Take(SimilarityLinkSample)
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Playlist not found.");

        // Nothing to be similar to. An empty row is better than a row of arbitrary playlists
        // dressed up as recommendations.
        if (subject.Tags.Count == 0 && subject.Links.Count == 0)
        {
            return [];
        }

        var candidates = db.Playlists.Where(p =>
            p.Visibility == PlaylistVisibility.Public && p.Id != subject.Id);

        candidates = candidates.VisibleTo(await prefs.ShowNsfwAsync(viewerId, ct));
        var visible = candidates.Select(p => p.Id);

        // Asked from the join tables rather than from the playlists.
        //
        // This used to walk every public playlist and, for each, count how many of its items and
        // tags appeared in the subject's arrays — evaluated per row, with no index able to help,
        // and then sorted on the result. The cost was (public playlists x their items x subject
        // links): a full cross-product that ran on every public playlist page view and degraded
        // to an empty row on failure, so it would have failed quietly under load.
        //
        // Starting from PlaylistItems and PlaylistTags asks the question the indexes answer —
        // LinkId and TagId are both indexed — and the counting happens in one GROUP BY over
        // matching rows rather than once per candidate.
        var sharedLinks = await db.PlaylistItems
            .Where(i => subject.Links.Contains(i.LinkId) && visible.Contains(i.PlaylistId))
            .GroupBy(i => i.PlaylistId)
            .Select(g => new { PlaylistId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var sharedTags = await db.PlaylistTags
            .Where(pt => subject.Tags.Contains(pt.TagId) && visible.Contains(pt.PlaylistId))
            .GroupBy(pt => pt.PlaylistId)
            .Select(g => new { PlaylistId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Two small lists of (playlist, count) merged here rather than in a third query. Shared
        // links are the stronger signal and keep their weight.
        var scores = new Dictionary<Guid, int>();
        foreach (var row in sharedLinks)
        {
            scores[row.PlaylistId] = row.Count * SharedLinkWeight;
        }

        foreach (var row in sharedTags)
        {
            scores[row.PlaylistId] = scores.GetValueOrDefault(row.PlaylistId) + row.Count;
        }

        if (scores.Count == 0)
        {
            return [];
        }

        var best = scores.OrderByDescending(s => s.Value).Take(take).Select(s => s.Key).ToList();

        var summaries = await candidates
            .Where(p => best.Contains(p.Id))
            .Select(Summarize())
            .ToListAsync(ct);

        // Ordered here because the scores live here, and the summary carries no id to match on —
        // which is why it travels beside one. The creation-date tiebreak keeps the row stable
        // between refreshes when two playlists score the same.
        return
        [
            .. summaries
                .OrderByDescending(s => scores.GetValueOrDefault(s.Id))
                .ThenByDescending(s => s.Row.CreationTime)
                .Select(s => s.Row),
        ];
    }

    /// <summary>How much more a shared link counts than a shared tag.</summary>
    private const int SharedLinkWeight = 3;

    /// <summary>
    /// The tags on public playlists that have seen activity lately, rather than the ones with the
    /// biggest all-time count — which is a list that never changes.
    /// </summary>
    public async Task<IReadOnlyList<TagSummary>> ListTrendingTagsAsync(
        int? days, CancellationToken ct = default)
    {
        var window = Math.Clamp(days ?? TrendingWindowDays, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-window);

        var rows = await db.PlaylistTags
            .Where(pt => pt.Playlist!.Visibility == PlaylistVisibility.Public
                && (pt.Playlist.CreationTime >= since
                    || pt.Playlist.Items.Any(i => i.CreationTime >= since)))
            .GroupBy(pt => pt.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(TagCounts.MaxResults)
            .ToListAsync(ct);

        return [.. rows.Select(r => new TagSummary(r.Name, r.Count))];
    }

    /// <summary>How far back "trending" looks when the caller doesn't say.</summary>
    public const int TrendingWindowDays = 30;

    public Task<IReadOnlyList<TagSummary>> ListPublicTagsAsync(string? q, CancellationToken ct = default) =>
        TagCounts.ListAsync(db.PlaylistTags.Where(pt => pt.Playlist!.Visibility == PlaylistVisibility.Public), q, ct);
}
