using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Tidying up tags.
/// </summary>
/// <remarks>
/// Tags arrive from four directions — by hand on a playlist, by hand on an item, from an
/// automation rule, and from an import — and until now none of it could be undone. A library
/// accumulated near-duplicates ("js" beside "javascript") and typos permanently, which quietly
/// degrades the tag filter, the trending row on discovery and every tag facet.
///
/// Note what a rename is and is not. <see cref="Tag"/> rows are global and unique on name:
/// everybody who tags something "rust" points at the same row. So renaming cannot mean renaming
/// that row — it would rename it under everybody. It means repointing this caller's own join
/// rows at a different tag, creating it if nobody has used it yet. Which makes rename and merge
/// the same operation, distinguished only by whether the destination already existed. That is
/// worth saying out loud in the response, because "merged into a tag you already had" and
/// "renamed to a fresh one" feel like different things to the person who asked.
/// </remarks>
public interface ITagManagementService
{
    /// <summary>Every tag the caller uses, with how many playlists and items carry it.</summary>
    Task<IReadOnlyList<TagUsage>> ListAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>Repoints the caller's uses of one tag at another, creating it if needed.</summary>
    Task<TagChange> RenameAsync(Guid ownerId, string from, string to, CancellationToken ct = default);

    /// <summary>Drops the caller's uses of a tag. The tag itself survives if others use it.</summary>
    Task<TagChange> RemoveAsync(Guid ownerId, string name, CancellationToken ct = default);
}

/// <inheritdoc />
public class TagManagementService(IAppDbContext db, ITagResolver tags) : ITagManagementService
{
    public async Task<IReadOnlyList<TagUsage>> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        // Two counts rather than one. GET /tags only ever counted playlists, so a tag applied
        // exclusively to items — which is most of them, since item tags are what search filters
        // on — appeared to have no uses at all. You cannot ask somebody to confirm a destructive
        // merge against a number that is wrong.
        var playlistCounts = await OwnPlaylistTags(ownerId)
            .GroupBy(pt => pt.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var itemCounts = await OwnItemTags(ownerId)
            .GroupBy(it => it.Tag!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var names = playlistCounts.Select(x => x.Name).Union(itemCounts.Select(x => x.Name));

        return
        [
            .. names
                .Select(name => new TagUsage(
                    name,
                    playlistCounts.FirstOrDefault(x => x.Name == name)?.Count ?? 0,
                    itemCounts.FirstOrDefault(x => x.Name == name)?.Count ?? 0))
                .OrderByDescending(t => t.PlaylistCount + t.ItemCount)
                .ThenBy(t => t.Name, StringComparer.Ordinal),
        ];
    }

    public async Task<TagChange> RenameAsync(
        Guid ownerId, string from, string to, CancellationToken ct = default)
    {
        var source = TagNormalizer.NormalizeOne(from);
        var target = TagNormalizer.NormalizeOne(to);

        if (source.Length == 0)
        {
            throw new ValidationException("from", "Which tag do you want to rename?");
        }

        if (target.Length == 0)
        {
            throw new ValidationException("to", "A tag needs a name. To get rid of one, delete it.");
        }

        if (source == target)
        {
            // Normalization makes "Rust" and "rust" the same request. That is a no-op rather than
            // an error: the person asked for a state that already holds.
            return new TagChange(0, 0);
        }

        var (playlistJoins, itemJoins) = await OwnUsesAsync(ownerId, source, ct);

        // Resolving the destination creates a Tag row, so only do it once the source is known to
        // be real and in use — otherwise a typo in the "to" box leaves an orphan tag behind.
        var merged = await db.Tags.AnyAsync(t => t.Name == target, ct);
        var targetTag = (await tags.ResolveAsync([target], ct)).Single();

        // Rows already carrying the destination as well. Repointing those would collide with the
        // unique pair index, and the honest answer is that the tag is already there — so they are
        // dropped rather than moved, which is what makes merging two overlapping tags work.
        var playlistsWithTarget = await OwnPlaylistTags(ownerId)
            .Where(pt => pt.TagId == targetTag.Id)
            .Select(pt => pt.PlaylistId)
            .ToListAsync(ct);

        var itemsWithTarget = await OwnItemTags(ownerId)
            .Where(it => it.TagId == targetTag.Id)
            .Select(it => it.PlaylistItemId)
            .ToListAsync(ct);

        foreach (var join in playlistJoins)
        {
            if (playlistsWithTarget.Contains(join.PlaylistId))
            {
                db.PlaylistTags.Remove(join);
            }
            else
            {
                join.TagId = targetTag.Id;
            }
        }

        foreach (var join in itemJoins)
        {
            if (itemsWithTarget.Contains(join.PlaylistItemId))
            {
                db.PlaylistItemTags.Remove(join);
            }
            else
            {
                join.TagId = targetTag.Id;
            }
        }

        await SaveAndTouchAsync(playlistJoins, itemJoins, ct);
        return new TagChange(playlistJoins.Count, itemJoins.Count, merged);
    }

    public async Task<TagChange> RemoveAsync(Guid ownerId, string name, CancellationToken ct = default)
    {
        var tag = TagNormalizer.NormalizeOne(name);
        if (tag.Length == 0)
        {
            throw new ValidationException("name", "Which tag?");
        }

        var (playlistJoins, itemJoins) = await OwnUsesAsync(ownerId, tag, ct);

        // Only this caller's uses, and only the join rows. The Tag row is shared, so deleting it
        // would take the tag off everybody else's playlists too; left alone and unused it is
        // invisible anyway, since every tag listing counts join rows rather than tags.
        db.PlaylistTags.RemoveRange(playlistJoins);
        db.PlaylistItemTags.RemoveRange(itemJoins);

        await SaveAndTouchAsync(playlistJoins, itemJoins, ct);
        return new TagChange(playlistJoins.Count, itemJoins.Count);
    }

    /// <summary>The caller's own join rows for a tag, or 404 when they don't use it.</summary>
    /// <remarks>
    /// Scoped to the owner throughout: these are global rows, and an unscoped query here would
    /// let anybody rename a tag off everybody else's playlists.
    /// </remarks>
    private async Task<(List<PlaylistTag> Playlists, List<PlaylistItemTag> Items)> OwnUsesAsync(
        Guid ownerId, string name, CancellationToken ct)
    {
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, ct)
            ?? throw new NotFoundException($"No tag called \"{name}\".");

        var playlists = await OwnPlaylistTags(ownerId).Where(pt => pt.TagId == tag.Id).ToListAsync(ct);
        var items = await OwnItemTags(ownerId).Where(it => it.TagId == tag.Id).ToListAsync(ct);

        if (playlists.Count == 0 && items.Count == 0)
        {
            // The tag exists, but somebody else's library is the only place it appears — which
            // from here is indistinguishable from it not existing, and should read the same.
            throw new NotFoundException($"No tag called \"{name}\".");
        }

        return (playlists, items);
    }

    private IQueryable<PlaylistTag> OwnPlaylistTags(Guid ownerId) =>
        db.PlaylistTags.Where(pt => pt.Playlist!.OwnerId == ownerId);

    private IQueryable<PlaylistItemTag> OwnItemTags(Guid ownerId) =>
        db.PlaylistItemTags.Where(it => it.PlaylistItem!.Playlist!.OwnerId == ownerId);

    /// <summary>
    /// Commits the join-row changes, then stamps the playlists and items they hang off.
    /// </summary>
    /// <remarks>
    /// Tags live in their own rows, so changing them leaves the playlist and item rows untouched
    /// and a client syncing on LastModified would never hear about it. Editing tags by hand
    /// already touches the parent for exactly this reason; a bulk rename has to as well, or the
    /// sync endpoint keeps handing back the old tags.
    ///
    /// Done as two statements rather than by loading each parent: a tag on three thousand items
    /// would otherwise materialize three thousand rows, notes and metadata included, to write one
    /// timestamp on each.
    /// </remarks>
    private async Task SaveAndTouchAsync(
        List<PlaylistTag> playlistJoins, List<PlaylistItemTag> itemJoins, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);

        var now = DateTimeOffset.UtcNow;

        if (playlistJoins.Count > 0)
        {
            var ids = playlistJoins.Select(j => j.PlaylistId).Distinct().ToList();
            await db.Playlists.Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.LastModified, now), ct);
        }

        if (itemJoins.Count > 0)
        {
            var ids = itemJoins.Select(j => j.PlaylistItemId).Distinct().ToList();
            await db.PlaylistItems.Where(i => ids.Contains(i.Id))
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.LastModified, now), ct);
        }
    }
}
