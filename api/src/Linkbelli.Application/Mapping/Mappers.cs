using Linkbelli.Contracts;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Mapping;

/// <summary>Manual entity → DTO mapping (no AutoMapper, by design).</summary>
public static class Mappers
{
    public static LinkResponse ToResponse(this Link link) => new(
        link.Id, link.CanonicalUrl, link.Host?.Hostname ?? string.Empty, link.Title, link.Description,
        link.ThumbnailUrl, link.SiteName, link.EnrichedAt != null, link.Nsfw, link.Host?.Favicon,
        link.EnrichmentStatus, link.EnrichmentError, link.WordCount, link.Kind, link.ArchiveUrl);

    /// <summary>
    /// A playlist as its owner sees it straight after writing to it.
    /// </summary>
    /// <remarks>
    /// Everything left unset is a thing this caller does not know and should not guess: the
    /// counts and the saved view come from queries the write path has no reason to run, and a
    /// zero would read as an answer rather than as a silence.
    /// </remarks>
    public static PlaylistResponse ToResponse(
        this Playlist playlist, int itemCount, IEnumerable<string> tags, bool nsfw,
        Guid? folderId = null, string? folderName = null) => new()
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Slug = playlist.Slug,
            Description = playlist.Description,
            Visibility = playlist.Visibility,
            ItemCount = itemCount,
            CreationTime = playlist.CreationTime,
            Tags = tags.ToArray(),
            Nsfw = nsfw,
            FolderId = folderId,
            FolderName = folderName,
            NsfwSetting = playlist.NsfwOverride switch
            {
                true => NsfwSetting.Yes,
                false => NsfwSetting.No,
                null => NsfwSetting.Auto,
            },
            ForkedFromPlaylistId = playlist.ForkedFromPlaylistId,
            IsOwner = true,
        };

    public static ApiKeyResponse ToResponse(this ApiKey key) => new(
        key.Id, key.Name, key.Prefix, key.Scopes, key.CreationTime, key.LastUsedAt, key.ExpiresAt);
}
