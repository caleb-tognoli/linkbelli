using Linkbelli.Application.Automation;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Webhooks;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Adds every address in a block of pasted text.
/// </summary>
/// <remarks>
/// The two existing ways in were one link at a time, and exporting a file to import it. What
/// people actually have in front of them is a chat log, a list of tabs, an email or a note.
/// </remarks>
public interface IPasteService
{
    Task<PasteResponse> PasteAsync(
        Guid ownerId, Guid playlistId, string? text, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PasteService(
    IAppDbContext db,
    ILinkService links,
    IPlaylistAccess access,
    IAutomationRunner automation,
    IWebhookEvents webhooks) : IPasteService
{
    public async Task<PasteResponse> PasteAsync(
        Guid ownerId, Guid playlistId, string? text, CancellationToken ct = default)
    {
        await access.EnsureAsync(ownerId, playlistId, PlaylistRole.Contributor, ct);

        var urls = UrlExtractor.Extract(text);
        if (urls.Count == 0)
        {
            throw new ValidationException("text", "No web addresses found in that.");
        }

        var existing = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .Select(i => i.LinkId)
            .ToListAsync(ct);

        var present = existing.ToHashSet();

        var nextPosition = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0;

        var added = new List<Guid>();
        var duplicates = 0;
        var rejected = new List<string>();

        foreach (var url in urls)
        {
            if (!UrlCanonicalizer.TryCanonicalize(url, out var canonical))
            {
                // Reported rather than dropped: a paste of forty links that silently becomes
                // thirty-eight is worse than one that says which two it could not read.
                rejected.Add(url);
                continue;
            }

            try
            {
                // Queued rather than fetched inline: a paste of a hundred links would otherwise
                // hold the request open for a hundred outbound fetches.
                var link = await links.GetOrCreateAsync(canonical, immediate: false, cancellationToken: ct);

                if (!present.Add(link.Id))
                {
                    duplicates++;
                    continue;
                }

                nextPosition += PlaylistOrdering.Gap;
                var item = new PlaylistItem
                {
                    PlaylistId = playlistId,
                    LinkId = link.Id,
                    Position = nextPosition,
                    Status = PlaylistItemStatus.Added,
                    AddedByUserId = ownerId,
                };

                db.PlaylistItems.Add(item);
                added.Add(item.Id);
            }
            catch (BlockedHostException)
            {
                rejected.Add(url);
            }
        }

        await db.SaveChangesAsync(ct);

        await webhooks.ItemsAddedAsync(added, ItemOrigin.Paste, ct);

        // The rules see a pasted link exactly as they see one added by hand.
        await automation.ApplyAsync(added, ct);

        return new PasteResponse(urls.Count, added.Count, duplicates, rejected);
    }
}
