using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Feeds;

/// <inheritdoc />
public class PlaylistFeedService(IAppDbContext db) : IPlaylistFeedService
{
    public async Task<FeedDocument> BuildAsync(
        string username, string slug, string selfUrl, string htmlUrl, CancellationToken ct = default)
    {
        var normalized = username.ToUpperInvariant();

        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.CreationTime,
                OwnerName = db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName!).First(),
                Nsfw = p.Items.Any(i => i.Link!.Nsfw),
            })
            .FirstOrDefaultAsync(ct);

        // A feed is fetched by a reader with no session, so there is no viewer to have opted in:
        // an NSFW playlist is simply not syndicated. Same rule the anonymous HTML read applies.
        if (playlist is null || playlist.Nsfw)
        {
            throw new NotFoundException("Playlist not found.");
        }

        var entries = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlist.Id && i.Link!.EnrichedAt != null && !i.Link.Nsfw)
            // Newest first: a feed is a stream, regardless of how the playlist is ordered on screen.
            .OrderByDescending(i => i.CreationTime)
            .ThenByDescending(i => i.Id)
            .Take(IPlaylistFeedService.MaxEntries)
            .Select(i => new
            {
                i.Id,
                i.CreationTime,
                i.Note,
                i.Metadata,
                Url = i.Link!.CanonicalUrl,
                i.Link.Title,
                i.Link.Description,
                i.Link.ThumbnailUrl,
            })
            .ToListAsync(ct);

        var feedEntries = entries.Select(e => new FeedEntry(
            e.Id.ToString(),
            // Source metadata is the more specific title when a source supplied one.
            Pick(e.Metadata, "title") ?? e.Title ?? e.Url,
            e.Url,
            // The owner's own note is the most useful summary they could have written.
            e.Note ?? e.Description,
            e.CreationTime,
            Pick(e.Metadata, "thumbnail") ?? e.ThumbnailUrl,
            Pick(e.Metadata, "author")))
            .ToList();

        return new FeedDocument(
            playlist.Name,
            playlist.Description,
            selfUrl,
            htmlUrl,
            playlist.OwnerName,
            // The newest entry is when the feed last changed; an empty playlist falls back to
            // its creation, so lastBuildDate is never missing.
            feedEntries.Count > 0 ? feedEntries[0].Published : playlist.CreationTime,
            feedEntries);
    }

    private static string? Pick(IReadOnlyDictionary<string, string>? metadata, string key) =>
        metadata is not null && metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
