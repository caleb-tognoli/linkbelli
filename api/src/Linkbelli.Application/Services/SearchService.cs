using System.Linq.Expressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Search;
using Linkbelli.Core.Tags;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Search across everything the caller owns. Searching inside one playlist only answers "where
/// in this list is it" — this answers "where did I put it", which is the question people
/// actually have once they own more than a handful of playlists.
/// </summary>
public interface ISearchService
{
    Task<PagedResult<SearchHit>> SearchAsync(Guid ownerId, SearchQuery query, CancellationToken ct = default);

    /// <summary>Hosts across the caller's items, most-saved first — the facets for a host filter.</summary>
    Task<IReadOnlyList<HostFacet>> ListHostsAsync(Guid ownerId, string? q, CancellationToken ct = default);

    /// <summary>Searches the caller has saved to come back to, newest first.</summary>
    Task<IReadOnlyList<SavedSearchResponse>> ListSavedAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// How many things each pinned search matches right now.
    /// </summary>
    /// <remarks>
    /// The count is the point. A saved search without one is a link; with one it is something you
    /// glance at. Capped at a handful because this is a count query each, on a request the app
    /// layout makes on every navigation.
    /// </remarks>
    Task<IReadOnlyList<PinnedSearch>> ListPinnedAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>Keeps one in the sidebar, or takes it out.</summary>
    Task<SavedSearchResponse> PinAsync(
        Guid ownerId, Guid id, bool pinned, CancellationToken ct = default);

    /// <summary>Saves a search under a name.</summary>
    Task<SavedSearchResponse> SaveAsync(Guid ownerId, SaveSearchRequest request, CancellationToken ct = default);

    /// <summary>Removes a saved search. The links it matched are untouched — it was only a question.</summary>
    Task DeleteSavedAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>Runs a saved search and returns what matches right now.</summary>
    Task<PagedResult<SearchHit>> RunSavedAsync(
        Guid ownerId, Guid id, int? limit, string? cursor, CancellationToken ct = default);
}

/// <summary>A site the caller has saved from, and how many of their links are on it.</summary>
public record HostFacet(string Hostname, int ItemCount);

/// <inheritdoc />
public class SearchService(IAppDbContext db, IUserPreferenceService prefs, IFullTextSearch text) : ISearchService
{
    private const int MaxLimit = 100;

    private static readonly Expression<Func<PlaylistItem, SearchHit>> ToHit = i => new SearchHit(
        i.Id,
        i.PlaylistId,
        i.Playlist!.Name,
        new LinkResponse(
            i.Link!.Id, i.Link.CanonicalUrl, i.Link.Host!.Hostname, i.Link.Title,
            i.Link.Description, i.Link.ThumbnailUrl, i.Link.SiteName, i.Link.EnrichedAt != null, i.Link.Nsfw,
            i.Link.Host.Favicon, i.Link.EnrichmentStatus, i.Link.EnrichmentError, i.Link.WordCount, i.Link.Kind,
                i.Link.ArchiveUrl),
        i.Note,
        i.Status,
        i.Score,
        i.CreationTime,
        i.StatusChangedAt,
        i.Tags.Select(t => t.Tag!.Name).ToArray(),
        // Snippet is filled in afterwards, from the search text; the rest comes off the row.
        null,
        i.ReadProgress,
        i.SnoozedUntil,
        i.SnoozeCount);

    /// <summary>
    /// Folds operators typed into the box into the filters that already existed.
    /// </summary>
    /// <remarks>
    /// Every one of these was reachable only as a chip on the search page, so the filter could be
    /// applied but not typed, not shared as a URL somebody else could read, and not saved as a
    /// sentence. <c>site:bbc.co.uk under:10 is:unread</c> is how people expect to ask this.
    ///
    /// A typed operator wins over the same filter passed as a parameter. Both are visible on the
    /// screen, and the box is the one being edited — somebody who types <c>site:</c> while a host
    /// chip is set has just said which one they mean.
    /// </remarks>
    private static SearchQuery WithOperators(SearchQuery query)
    {
        var parsed = SearchOperators.Parse(query.Q);

        return query with
        {
            Q = parsed.Text,
            Host = parsed.Host ?? query.Host,
            ItemTags = parsed.ItemTags.Count > 0
                ? [.. parsed.ItemTags.Concat(query.ItemTags ?? [])]
                : query.ItemTags,
            Status = parsed.Status ?? query.Status,
            Broken = parsed.Broken ?? query.Broken,
            Kind = parsed.Kind ?? query.Kind,
            MinScore = parsed.MinScore ?? query.MinScore,
            MaxMinutes = parsed.MaxMinutes ?? query.MaxMinutes,
        };
    }

    public async Task<PagedResult<SearchHit>> SearchAsync(Guid ownerId, SearchQuery raw, CancellationToken ct = default)
    {
        var query = WithOperators(raw);
        var take = Paging.Take(query.Limit, MaxLimit, fallback: 25);
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        var items = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);
        if (!showNsfw) items = items.Where(i => !i.Link!.Nsfw);

        // Put aside, and not due back yet. "Not now" has to mean it is actually gone for a while
        // or it is a button that changes nothing — so this holds across search, not only in the
        // queue. Asking for them specifically is how you get them back.
        var due = DateTimeOffset.UtcNow;
        items = query.Snoozed == true
            ? items.Where(i => i.SnoozedUntil != null && i.SnoozedUntil > due)
            : items.Where(i => i.SnoozedUntil == null || i.SnoozedUntil <= due);

        items = ApplyText(items, query.Q);
        items = ApplyHost(items, query.Host);
        items = ApplyTags(items, query.Tags);
        items = ApplyItemTags(items, query.ItemTags);
        items = ApplyStatus(items, query.Status);

        if (query.MinScore is { } minScore)
        {
            items = items.Where(i => i.Score != null && i.Score >= minScore);
        }

        if (query.Broken == true)
        {
            // Both outcomes count as rot from the reader's side: the page is gone, or it can no
            // longer be read. Either way the saved link no longer gives them what they saved.
            items = items.Where(i => i.Link!.EnrichmentStatus == EnrichmentStatus.Broken
                || i.Link.EnrichmentStatus == EnrichmentStatus.Failed);
        }

        items = ApplyKind(items, query.Kind);

        if (query.MaxMinutes is { } minutes)
        {
            // Articles only, and only ones long enough to have been read at all: "under five
            // minutes" is a question about reading, and a link with no article behind it has no
            // length to compare. Rounded the same way the reading time on the row is.
            var words = ReadingTime.WordsWithin(minutes);
            items = items.Where(i => i.Link!.WordCount != null && i.Link.WordCount <= words);
        }

        if (query.FinishedSince is { } since)
        {
            items = items.Where(i => i.Status == PlaylistItemStatus.Watched
                && i.StatusChangedAt != null
                && i.StatusChangedAt >= since);
        }

        var continuing = Common.Cursor.DecodePage(query.Cursor, out var total, out var payload);
        if (!continuing)
        {
            total = await items.CountAsync(ct);
        }

        var offset = Common.Cursor.ParseOffset(payload);

        // Offset paging: the ordering is a computed relevance bucket with no stored column to
        // key on. A search is read a page or two deep, so the offset stays small in practice.
        var rows = await Order(items, query.Q, query.Sort)
            .Skip(offset)
            .Take(take + 1)
            .Select(ToHit)
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Common.Cursor.EncodePage(total, (offset + take).ToString());
        }

        rows = await WithSnippetsAsync(rows, query.Q, ct);

        return new PagedResult<SearchHit>(rows, next) { Total = total };
    }

    public async Task<IReadOnlyList<HostFacet>> ListHostsAsync(Guid ownerId, string? q, CancellationToken ct = default)
    {
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        var items = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);
        if (!showNsfw) items = items.Where(i => !i.Link!.Nsfw);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.ToLower();
            items = items.Where(i => i.Link!.Host!.Hostname.ToLower().Contains(needle));
        }

        // Ordered on the grouping itself, not on a projected record's properties — EF cannot
        // translate an OrderBy that reaches into a type it just constructed.
        var grouped = await items
            .GroupBy(i => i.Link!.Host!.Hostname)
            .Select(g => new { Hostname = g.Key, ItemCount = g.Count() })
            .OrderByDescending(x => x.ItemCount)
            .ThenBy(x => x.Hostname)
            .Take(50)
            .ToListAsync(ct);

        return grouped.Select(x => new HostFacet(x.Hostname, x.ItemCount)).ToList();
    }

    public async Task<IReadOnlyList<SavedSearchResponse>> ListSavedAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.SavedSearches
            .Where(ss => ss.OwnerId == ownerId)
            .OrderByDescending(ss => ss.CreationTime)
            .Select(ss => new SavedSearchResponse(
                ss.Id, ss.Name, ss.Query, ss.Host, ss.Tags, ss.ItemTags,
                ss.Status, ss.MinScore, ss.Broken, ss.Sort, ss.CreationTime,
                ss.Kind, ss.MaxMinutes, ss.Pinned))
            .ToListAsync(ct);

    /// <summary>
    /// The most that can be kept in the sidebar.
    /// </summary>
    /// <remarks>
    /// Each one is a count query on a request the layout makes on every navigation, so this is a
    /// budget rather than a taste. It is also about as many as a sidebar can show before it stops
    /// being a sidebar.
    /// </remarks>
    public const int MaxPinned = 5;

    public async Task<IReadOnlyList<PinnedSearch>> ListPinnedAsync(
        Guid ownerId, CancellationToken ct = default)
    {
        var pinned = await db.SavedSearches
            .Where(ss => ss.OwnerId == ownerId && ss.Pinned)
            .OrderBy(ss => ss.Name)
            .Take(MaxPinned)
            .ToListAsync(ct);

        var counts = new List<PinnedSearch>(pinned.Count);
        foreach (var saved in pinned)
        {
            // One page of one, for the total. SearchAsync counts the filtered set on a first
            // page and carries it, so asking for the smallest page is asking for the count.
            var page = await SearchAsync(ownerId, ToQuery(saved, limit: 1, cursor: null), ct);
            counts.Add(new PinnedSearch(saved.Id, saved.Name, page.Total ?? 0));
        }

        return counts;
    }

    public async Task<SavedSearchResponse> PinAsync(
        Guid ownerId, Guid id, bool pinned, CancellationToken ct = default)
    {
        var saved = await db.SavedSearches.FirstOrDefaultAsync(ss => ss.Id == id && ss.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Saved search not found.");

        if (pinned && !saved.Pinned)
        {
            var already = await db.SavedSearches.CountAsync(ss => ss.OwnerId == ownerId && ss.Pinned, ct);
            if (already >= MaxPinned)
            {
                throw new ValidationException(
                    "pinned",
                    $"You can keep {MaxPinned} searches in the sidebar. Take one out first.");
            }
        }

        saved.Pinned = pinned;
        await db.SaveChangesAsync(ct);

        return new SavedSearchResponse(
            saved.Id, saved.Name, saved.Query, saved.Host, saved.Tags, saved.ItemTags,
            saved.Status, saved.MinScore, saved.Broken, saved.Sort, saved.CreationTime,
            saved.Kind, saved.MaxMinutes, saved.Pinned);
    }

    /// <summary>
    /// A saved search as the query it stands for.
    /// </summary>
    /// <remarks>
    /// Run now rather than as of when it was saved: that is the whole point of keeping the
    /// question rather than the answer.
    /// </remarks>
    private static SearchQuery ToQuery(SavedSearch saved, int? limit, string? cursor) =>
        new(saved.Query, saved.Host, saved.Tags, saved.ItemTags, saved.Status, saved.MinScore,
            FinishedSince: null, saved.Broken ? true : null, saved.Kind, saved.MaxMinutes,
            saved.Sort, limit, cursor);

    public async Task<SavedSearchResponse> SaveAsync(
        Guid ownerId, SaveSearchRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "A name is required.");
        }

        var saved = new SavedSearch
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Query = request.Q?.Trim(),
            Host = request.Host?.Trim().ToLowerInvariant(),
            Tags = TagNormalizer.Normalize(request.Tags ?? []).ToArray(),
            ItemTags = TagNormalizer.Normalize(request.ItemTags ?? []).ToArray(),
            Status = request.Status?.Trim(),
            MinScore = request.MinScore,
            Broken = request.Broken,
            Sort = request.Sort?.Trim(),
            Kind = request.Kind?.Trim(),
            MaxMinutes = request.MaxMinutes,
        };

        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(ct);

        return new SavedSearchResponse(
            saved.Id, saved.Name, saved.Query, saved.Host, saved.Tags, saved.ItemTags,
            saved.Status, saved.MinScore, saved.Broken, saved.Sort, saved.CreationTime,
            saved.Kind, saved.MaxMinutes);
    }

    public async Task DeleteSavedAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var saved = await db.SavedSearches.FirstOrDefaultAsync(ss => ss.Id == id && ss.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Saved search not found.");

        // Soft, like every other delete here. The links it matched were never owned by it.
        db.SavedSearches.Remove(saved);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<SearchHit>> RunSavedAsync(
        Guid ownerId, Guid id, int? limit, string? cursor, CancellationToken ct = default)
    {
        var saved = await db.SavedSearches.FirstOrDefaultAsync(ss => ss.Id == id && ss.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Saved search not found.");

        return await SearchAsync(ownerId, ToQuery(saved, limit, cursor), ct);
    }

    /// <summary>
    /// The same predicate the in-playlist search uses, so both are served by the trigram indexes
    /// added in AddSearchIndexes. A pasted URL short-circuits to the indexed dedup hash.
    /// </summary>
    /// <summary>
    /// Narrows to one kind of thing. An unrecognised name matches nothing rather than everything,
    /// so a typo returns an empty list instead of quietly ignoring the filter.
    /// </summary>
    private static IQueryable<PlaylistItem> ApplyKind(IQueryable<PlaylistItem> items, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return items;
        }

        return Enum.TryParse<ContentKind>(kind.Trim(), ignoreCase: true, out var parsed)
            ? items.Where(i => i.Link!.Kind == parsed)
            : items.Where(_ => false);
    }

    /// <summary>Characters of article text shown around a match.</summary>
    private const int SnippetLength = 240;

    /// <summary>
    /// Adds the sentence a term was found in, for hits that matched on the article text alone.
    /// A hit whose title doesn't contain the word looks like a mistake without it.
    /// </summary>
    /// <remarks>
    /// One extra query for the page that was just read, and only the window around each match
    /// comes back — the stored articles themselves are far too big to pull into memory to slice.
    /// </remarks>
    private async Task<List<SearchHit>> WithSnippetsAsync(List<SearchHit> rows, string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || rows.Count == 0)
        {
            return rows;
        }

        var needle = q.ToLower();
        var unexplained = rows
            .Where(hit => !Explains(hit.Link.Title, needle) && !Explains(hit.Note, needle))
            .Select(hit => hit.Link.Id)
            .ToList();

        if (unexplained.Count == 0)
        {
            return rows;
        }

        // Asked of the search implementation rather than by hunting the literal words in the
        // text: matching is stemmed, so a hit for "railways" may be an article that only ever
        // says "railway" — and cutting a window at the first literal occurrence cannot find one.
        var snippets = await text.SnippetsAsync(unexplained, q, SnippetLength, ct);

        return [.. rows.Select(hit =>
            snippets.TryGetValue(hit.Link.Id, out var snippet) ? hit with { Snippet = snippet } : hit)];
    }

    /// <summary>Whether what is already on screen accounts for the match.</summary>
    private static bool Explains(string? value, string needle) =>
        value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private IQueryable<PlaylistItem> ApplyText(IQueryable<PlaylistItem> items, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return items;

        // A pasted address is an exact question, and the dedup hash already answers it off an
        // index. Kept ahead of the text search, which would stem a URL into nonsense.
        if (UrlCanonicalizer.TryCanonicalize(q, out var canonical))
        {
            var hash = canonical.Hash;
            return items.Where(i => i.Link!.UrlHash == hash);
        }

        // The article itself is in here too, so "that piece about the Dutch railways" finds
        // it even when neither word is in the title — the difference is that it is now a GIN
        // index lookup rather than a substring scan over every stored article.
        return text.Match(items, q);
    }

    private static IQueryable<PlaylistItem> ApplyHost(IQueryable<PlaylistItem> items, string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return items;

        var hostname = host.Trim().ToLowerInvariant();
        return items.Where(i => i.Link!.Host!.Hostname == hostname);
    }

    /// <summary>Every tag must be present (AND), matching how the playlist list filters.</summary>
    private static IQueryable<PlaylistItem> ApplyTags(IQueryable<PlaylistItem> items, string[]? tags)
    {
        if (tags is null || tags.Length == 0) return items;

        foreach (var raw in tags)
        {
            var tag = TagNormalizer.NormalizeOne(raw);
            if (string.IsNullOrEmpty(tag)) continue;

            items = items.Where(i => i.Playlist!.Tags.Any(pt => pt.Tag!.Name == tag));
        }

        return items;
    }

    /// <summary>
    /// Every tag must be present (AND). Tags on the link itself, as opposed to on the list it
    /// happens to sit in — which is what makes something findable across lists.
    /// </summary>
    private static IQueryable<PlaylistItem> ApplyItemTags(IQueryable<PlaylistItem> items, string[]? tags)
    {
        if (tags is null || tags.Length == 0) return items;

        foreach (var raw in tags)
        {
            var tag = TagNormalizer.NormalizeOne(raw);
            if (string.IsNullOrEmpty(tag)) continue;

            items = items.Where(i => i.Tags.Any(t => t.Tag!.Name == tag));
        }

        return items;
    }

    private static IQueryable<PlaylistItem> ApplyStatus(IQueryable<PlaylistItem> items, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return items;
        if (status.Equals("watched", StringComparison.OrdinalIgnoreCase))
            return items.Where(i => i.Status == PlaylistItemStatus.Watched);
        if (status.Equals("unwatched", StringComparison.OrdinalIgnoreCase))
            return items.Where(i => i.Status == PlaylistItemStatus.Added);
        return items;
    }

    /// <summary>
    /// Relevance when there is a term to rank by — title, then site name, then everything else —
    /// and most recently added otherwise, which is what a bare browse wants.
    /// </summary>
    private IQueryable<PlaylistItem> Order(IQueryable<PlaylistItem> items, string? q, string? sort)
    {
        if (string.Equals(sort, "queue", StringComparison.OrdinalIgnoreCase))
        {
            // "What should I read next": something already started comes first, because the
            // cheapest thing to finish is the thing you are part way through. Then what you
            // rated highly, then among the unrated the ones you have been carrying longest —
            // a queue that always surfaces the newest arrival is how a backlog becomes permanent.
            return items
                .OrderBy(i => i.ReadProgress == null ? 1 : 0)
                .ThenByDescending(i => i.ReadProgress)
                .ThenBy(i => i.Score == null ? 1 : 0)
                .ThenByDescending(i => i.Score)
                .ThenBy(i => i.CreationTime)
                .ThenBy(i => i.Id);
        }

        if (string.Equals(sort, "score", StringComparison.OrdinalIgnoreCase))
        {
            // Unrated items sort last rather than as zero: "not rated" isn't "rated badly".
            return items
                .OrderBy(i => i.Score == null ? 1 : 0)
                .ThenByDescending(i => i.Score)
                .ThenByDescending(i => i.CreationTime);
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return items.OrderByDescending(i => i.CreationTime).ThenByDescending(i => i.Id);
        }

        // Relevance from the same weighted vector the match used, rather than three more
        // substring scans in the ORDER BY approximating "title beats site name beats body".
        // The weighting says that properly, and cover density also accounts for how close the
        // matched words sit to each other.
        return text.OrderByRelevance(items, q);
    }
}
