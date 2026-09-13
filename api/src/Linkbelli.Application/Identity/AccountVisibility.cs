using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Identity;

/// <summary>
/// Taking an account's public content down, and putting it back.
/// </summary>
/// <remarks>
/// Two things need this and neither is a deletion: an account asked to be deleted has a grace
/// period during which its owner has said "stop showing my things", and a suspended account has
/// had that said about it. Both want the same effect and both have to be reversible.
///
/// Done by setting every playlist Private rather than by filtering reads. Visibility is the one
/// thing every public path already checks — discovery, the profile page, the sitemap, the three
/// feed formats, the tag facets, the similar-playlists query — so this takes effect in all of
/// them at once, with no call site left to forget and no subquery added to the hottest entity in
/// the schema.
/// </remarks>
public static class AccountVisibility
{
    /// <summary>Takes every playlist this account publishes out of view, remembering what it was.</summary>
    public static async Task HideAsync(IAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        await db.Playlists
            .Where(p => p.OwnerId == userId
                && p.Visibility != PlaylistVisibility.Private
                // Only the ones not already hidden: hiding twice would overwrite the memory of
                // what they were with Private, and there would be nothing to restore.
                && p.VisibilityBeforeHiding == null)
            .ExecuteUpdateAsync(
                u => u
                    .SetProperty(p => p.VisibilityBeforeHiding, p => p.Visibility)
                    .SetProperty(p => p.Visibility, PlaylistVisibility.Private),
                ct);
    }

    /// <summary>Puts back exactly what was published before, and nothing else.</summary>
    public static async Task RestoreAsync(IAppDbContext db, Guid userId, CancellationToken ct = default)
    {
        await db.Playlists
            .Where(p => p.OwnerId == userId && p.VisibilityBeforeHiding != null)
            .ExecuteUpdateAsync(
                u => u
                    .SetProperty(p => p.Visibility, p => p.VisibilityBeforeHiding!.Value)
                    .SetProperty(p => p.VisibilityBeforeHiding, (PlaylistVisibility?)null),
                ct);
    }
}
