using System.Security.Cryptography;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Sharing one saved link. Until now the only way was to make the whole playlist public, or to
/// send a bare address — which loses the note that was usually the reason for sending it.
/// </summary>
public interface IItemShareService
{
    /// <summary>Starts sharing an item, or returns the link it is already shared under.</summary>
    Task<ItemShareResponse> ShareAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);

    /// <summary>Stops sharing it. Links already sent stop working, which is the point.</summary>
    Task RevokeAsync(Guid ownerId, Guid itemId, CancellationToken ct = default);

    /// <summary>Reads a shared item by its token. Anonymous — this is what the link opens.</summary>
    Task<SharedItemResponse> GetAsync(string token, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ItemShareService(IAppDbContext db) : IItemShareService
{
    /// <summary>
    /// Bytes of randomness in a token. 128 bits: the token is the only thing protecting what it
    /// opens, and share links get forwarded, indexed and guessed at.
    /// </summary>
    private const int TokenBytes = 16;

    public async Task<ItemShareResponse> ShareAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await FindOwnedAsync(ownerId, itemId, ct);

        // Sharing twice gives the same link back rather than invalidating the one already sent.
        if (item.ShareToken is null)
        {
            item.ShareToken = NewToken();
            item.SharedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new ItemShareResponse(item.Id, item.ShareToken, item.SharedAt);
    }

    public async Task RevokeAsync(Guid ownerId, Guid itemId, CancellationToken ct = default)
    {
        var item = await FindOwnedAsync(ownerId, itemId, ct);
        if (item.ShareToken is null)
        {
            return;
        }

        item.ShareToken = null;
        item.SharedAt = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<SharedItemResponse> GetAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new NotFoundException("That link has expired or was never shared.");
        }

        var shared = await db.PlaylistItems
            .AsNoTracking()
            .Include(i => i.Link).ThenInclude(l => l!.Host)
            .Include(i => i.Playlist)
            .Where(i => i.ShareToken == token)
            .Select(i => new
            {
                Item = i,
                // The owner's name, because a shared link with nobody behind it reads as spam.
                OwnerUsername = db.Users
                    .Where(u => u.Id == i.Playlist!.OwnerId)
                    .Select(u => u.UserName)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct)
            // Deliberately the same answer as a token that never existed: a revoked share should
            // not confirm that it once pointed at something.
            ?? throw new NotFoundException("That link has expired or was never shared.");

        var item = shared.Item;

        return new SharedItemResponse(
            item.Link!.CanonicalUrl,
            item.Link.Host!.Hostname,
            item.Metadata?.GetValueOrDefault("title") ?? item.Link.Title,
            item.Link.Description,
            item.Link.ThumbnailUrl is null ? null : item.Link.Id,
            item.Link.SiteName,
            item.Note,
            shared.OwnerUsername ?? "someone",
            item.SharedAt ?? item.CreationTime,
            item.Link.Nsfw,
            item.Link.Kind,
            item.Link.WordCount);
    }

    private async Task<PlaylistItem> FindOwnedAsync(Guid ownerId, Guid itemId, CancellationToken ct) =>
        await db.PlaylistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.Playlist!.OwnerId == ownerId, ct)
        ?? throw new NotFoundException("Item not found.");

    /// <summary>URL-safe and short enough to paste into a message.</summary>
    private static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
