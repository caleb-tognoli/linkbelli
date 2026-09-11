using System.Linq.Expressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
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
public class SearchService(IAppDbContext db, IUserPreferenceService prefs) : ISearchService
{
    private const int MaxLimit = 100;

    private static readonly Expression<Func<PlaylistItem, SearchHit>> ToHit = i => new SearchHit(
        i.Id,
        i.PlaylistId,
        i.Playlist!.Name,
        new LinkResponse(
            i.Link!.Id, i.Link.CanonicalUrl, i.Link.Host!.Hostname, i.Link.Title,
            i.Link.Description, i.Link.ThumbnailUrl, i.Link.SiteName, i.Link.EnrichedAt != null, i.Link.Nsfw,
            i.Link.Host.Favicon, i.Link.EnrichmentStatus, i.Link.EnrichmentError, i.Link.WordCount),
        i.Note,
        i.Status,
        i.Score,
        i.CreationTime,
        i.StatusChangedAt,
        i.Tags.Select(t => t.Tag!.Name).ToArray());

    public async Task<PagedResult<SearchHit>> SearchAsync(Guid ownerId, SearchQuery query, CancellationToken ct = default)
    {
        var take = Math.Clamp(query.Limit ?? 25, 1, MaxLimit);
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        var items = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);
        if (!showNsfw) items = items.Where(i => !i.Link!.Nsfw);

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

        if (query.FinishedSince is { } since)
        {
            items = items.Where(i => i.Status == PlaylistItemStatus.Watched
                && i.StatusChangedAt != null
                && i.StatusChangedAt >= since);
        }

        var continuing = Common.Cursor.TryDecodePage(query.Cursor, out var total, out var payload);
        if (!continuing)
        {
            total = await items.CountAsync(ct);
        }

        var offset = int.TryParse(payload, out var parsed) ? parsed : 0;

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
                ss.Status, ss.MinScore, ss.Broken, ss.Sort, ss.CreationTime))
            .ToListAsync(ct);

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
        };

        db.SavedSearches.Add(saved);
        await db.SaveChangesAsync(ct);

        return new SavedSearchResponse(
            saved.Id, saved.Name, saved.Query, saved.Host, saved.Tags, saved.ItemTags,
            saved.Status, saved.MinScore, saved.Broken, saved.Sort, saved.CreationTime);
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

        // Run now, not as of when it was saved: that is the whole point of saving the question
        // rather than the answer.
        return await SearchAsync(ownerId, new SearchQuery(
            saved.Query, saved.Host, saved.Tags, saved.ItemTags, saved.Status, saved.MinScore,
            FinishedSince: null, saved.Broken ? true : null, saved.Sort, limit, cursor), ct);
    }

    /// <summary>
    /// The same predicate the in-playlist search uses, so both are served by the trigram indexes
    /// added in AddSearchIndexes. A pasted URL short-circuits to the indexed dedup hash.
    /// </summary>
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

        var snippets = (await db.Links
            .AsNoTracking()
            .Where(l => unexplained.Contains(l.Id) && l.Content != null && l.Content.ToLower().Contains(needle))
            .Select(l => new
            {
                l.Id,
                Text = l.Content!.Substring(l.Content.ToLower().IndexOf(needle), SnippetLength),
            })
            .ToListAsync(ct))
            .ToDictionary(row => row.Id, row => row.Text);

        return [.. rows.Select(hit =>
            snippets.TryGetValue(hit.Link.Id, out var snippet) ? hit with { Snippet = snippet } : hit)];
    }

    /// <summary>Whether what is already on screen accounts for the match.</summary>
    private static bool Explains(string? value, string needle) =>
        value is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static IQueryable<PlaylistItem> ApplyText(IQueryable<PlaylistItem> items, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return items;

        if (UrlCanonicalizer.TryCanonicalize(q, out var canonical))
        {
            var hash = canonical.Hash;
            return items.Where(i => i.Link!.UrlHash == hash);
        }

        var needle = q.ToLower();
        return items.Where(i =>
            (i.Link!.Title != null && i.Link.Title.ToLower().Contains(needle))
            || (i.Link!.Description != null && i.Link.Description.ToLower().Contains(needle))
            || (i.Link!.SiteName != null && i.Link.SiteName.ToLower().Contains(needle))
            || (i.Note != null && i.Note.ToLower().Contains(needle))
            // The article itself, so "that piece about the Dutch railways" finds it even when
            // neither of those words is in the title.
            || (i.Link!.Content != null && i.Link.Content.ToLower().Contains(needle))
            || i.Link!.CanonicalUrl.ToLower().Contains(needle)
            || i.Link!.Host!.Hostname.ToLower().Contains(needle));
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
    private static IQueryable<PlaylistItem> Order(IQueryable<PlaylistItem> items, string? q, string? sort)
    {
        if (string.Equals(sort, "queue", StringComparison.OrdinalIgnoreCase))
        {
            // "What should I read next": things you rated highly come first, and among the
            // unrated, the ones you have been carrying longest — because a queue that always
            // surfaces the newest arrival is how a backlog becomes permanent.
            return items
                .OrderBy(i => i.Score == null ? 1 : 0)
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

        var needle = q.ToLower();
        return items
            .OrderBy(i => i.Link!.Title != null && i.Link.Title.ToLower().Contains(needle) ? 0
                : i.Link!.SiteName != null && i.Link.SiteName.ToLower().Contains(needle) ? 1
                : 2)
            .ThenByDescending(i => i.CreationTime)
            .ThenByDescending(i => i.Id);
    }
}
