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
/// <remarks>
/// Both groupings happen in the database. They used to happen in memory, over every playlist item
/// the caller owns — id, playlist name, the full canonical URL, title and timestamp for each —
/// fetched on every visit to a page that usually renders "Nothing saved twice". The 200-group cap
/// was applied afterwards, so it bounded the output and not the work.
///
/// The shape here is: find the keys that are duplicated, cap those, then fetch the rows for the
/// ones that survived. So the amount pulled back is proportional to what is actually shown.
/// </remarks>
public class DuplicateService(IAppDbContext db, IUserPreferenceService prefs) : IDuplicateService
{
    /// <summary>Where the link's host+path is stored. A shadow property; see LinkbelliDbContext.</summary>
    private const string HostPathProperty = "HostPath";

    public async Task<IReadOnlyList<DuplicateGroup>> FindAsync(Guid ownerId, CancellationToken ct = default)
    {
        var showNsfw = await prefs.ShowNsfwAsync(ownerId, ct);

        var mine = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);
        if (!showNsfw) mine = mine.Where(i => !i.Link!.Nsfw);

        // The identical link in more than one playlist. Within one playlist this cannot happen —
        // a unique index stops it — so every group here spans lists.
        var sameLinkIds = await mine
            .GroupBy(i => i.LinkId)
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(IDuplicateService.MaxGroups)
            .ToListAsync(ct);

        var groups = new List<DuplicateGroup>();

        if (sameLinkIds.Count > 0)
        {
            foreach (var group in (await Copies(mine, i => sameLinkIds.Contains(i.LinkId), ct))
                .GroupBy(c => c.LinkId))
            {
                groups.Add(new DuplicateGroup(
                    DuplicateKind.SameLink,
                    group.First().Url,
                    [.. group.Select(ToCopy).OrderBy(c => c.AddedAt)]));
            }
        }

        // The same page under different addresses, grouped on the stored host+path — what stays
        // the same when a link arrives with a query string nobody has taught us to strip. Links
        // already counted above are excluded, so a link in three playlists is one group and not
        // also part of a second.
        var remaining = mine.Where(i => !sameLinkIds.Contains(i.LinkId));

        var samePageKeys = await remaining
            .GroupBy(i => EF.Property<string>(i.Link!, HostPathProperty))
            .Where(g => g.Select(i => i.LinkId).Distinct().Count() > 1)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(IDuplicateService.MaxGroups)
            .ToListAsync(ct);

        if (samePageKeys.Count > 0)
        {
            var copies = await Copies(
                remaining, i => samePageKeys.Contains(EF.Property<string>(i.Link!, HostPathProperty)), ct);

            foreach (var group in copies.GroupBy(c => c.HostPath))
            {
                groups.Add(new DuplicateGroup(
                    DuplicateKind.SamePage,
                    group.Key,
                    [.. group.Select(ToCopy).OrderBy(c => c.AddedAt)]));
            }
        }

        return
        [
            .. groups
                // Worst offenders first: the biggest pile-ups are the ones worth clearing.
                .OrderByDescending(g => g.Copies.Count)
                .ThenBy(g => g.Key)
                .Take(IDuplicateService.MaxGroups)
        ];
    }

    private static Task<List<SavedCopy>> Copies(
        IQueryable<Linkbelli.Core.Entities.PlaylistItem> items,
        System.Linq.Expressions.Expression<Func<Linkbelli.Core.Entities.PlaylistItem, bool>> matching,
        CancellationToken ct) =>
        items.Where(matching)
            .Select(i => new SavedCopy(
                i.Id, i.PlaylistId, i.Playlist!.Name, i.LinkId,
                i.Link!.CanonicalUrl, i.Link.Title, i.CreationTime,
                EF.Property<string>(i.Link!, HostPathProperty)))
            .ToListAsync(ct);

    /// <summary>What the grouping needs from each saved row, projected in the database.</summary>
    private record SavedCopy(
        Guid Id, Guid PlaylistId, string PlaylistName, Guid LinkId,
        string Url, string? Title, DateTimeOffset CreationTime, string HostPath);

    private static DuplicateCopy ToCopy(SavedCopy saved) =>
        new(saved.Id, saved.PlaylistId, saved.PlaylistName, saved.Url, saved.Title, saved.CreationTime);
}
