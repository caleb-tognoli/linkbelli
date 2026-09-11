using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Finds the same thing saved more than once. Dedup already prevents the identical link landing
/// twice in one playlist, and canonicalization strips the tracking parameters it knows about —
/// neither helps with the same page saved to three lists, or reached by an address the
/// canonicalizer has never seen.
/// </summary>
public interface IDuplicateService
{
    /// <summary>Groups returned at most, so one very messy collection can't produce an unbounded page.</summary>
    const int MaxGroups = 200;

    Task<IReadOnlyList<DuplicateGroup>> FindAsync(Guid ownerId, CancellationToken ct = default);
}

/// <inheritdoc />
public class DuplicateService(IAppDbContext db, IUserPreferenceService prefs) : IDuplicateService
{
    public async Task<IReadOnlyList<DuplicateGroup>> FindAsync(Guid ownerId, CancellationToken ct = default)
    {
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        var query = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);
        if (!showNsfw) query = query.Where(i => !i.Link!.Nsfw);

        var saved = await query
            .Select(i => new SavedCopy(
                i.Id, i.PlaylistId, i.Playlist!.Name, i.LinkId,
                i.Link!.CanonicalUrl, i.Link.Title, i.CreationTime))
            .ToListAsync(ct);

        var groups = new List<DuplicateGroup>();

        // The identical link in more than one playlist. Within one playlist this can't happen —
        // there is a unique index stopping it — so every group here spans lists.
        foreach (var group in saved.GroupBy(s => s.LinkId).Where(g => g.Count() > 1))
        {
            groups.Add(new DuplicateGroup(
                DuplicateKind.SameLink,
                group.First().Url,
                group.Select(ToCopy).OrderBy(c => c.AddedAt).ToList()));
        }

        // The same page under different addresses. Grouped on host + path, which is what stays
        // the same when a link arrives with a query string nobody has taught us to strip.
        var alreadyGrouped = groups.SelectMany(g => g.Copies).Select(c => c.ItemId).ToHashSet();

        foreach (var group in saved
            .Where(s => !alreadyGrouped.Contains(s.Id))
            .GroupBy(s => HostAndPath(s.Url))
            .Where(g => g.Key is not null && g.Select(s => s.LinkId).Distinct().Count() > 1))
        {
            groups.Add(new DuplicateGroup(
                DuplicateKind.SamePage,
                group.Key!,
                group.Select(ToCopy).OrderBy(c => c.AddedAt).ToList()));
        }

        return groups
            // Worst offenders first: the biggest pile-ups are the ones worth clearing.
            .OrderByDescending(g => g.Copies.Count)
            .ThenBy(g => g.Key)
            .Take(IDuplicateService.MaxGroups)
            .ToList();
    }

    /// <summary>What the grouping needs from each saved row, projected in the database.</summary>
    private record SavedCopy(
        Guid Id, Guid PlaylistId, string PlaylistName, Guid LinkId,
        string Url, string? Title, DateTimeOffset CreationTime);

    private static DuplicateCopy ToCopy(SavedCopy saved) =>
        new(saved.Id, saved.PlaylistId, saved.PlaylistName, saved.Url, saved.Title, saved.CreationTime);

    /// <summary>
    /// The part of an address that identifies the page rather than how you arrived at it. Null
    /// when the URL won't parse, which keeps unparseable rows out of the grouping entirely.
    /// </summary>
    private static string? HostAndPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        // A trailing slash is not a different page.
        var path = parsed.AbsolutePath.TrimEnd('/');
        return $"{parsed.Host}{path}";
    }
}
