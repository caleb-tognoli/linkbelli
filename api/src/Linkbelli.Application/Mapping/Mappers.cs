using Linkbelli.Contracts;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Mapping;

/// <summary>Manual entity → DTO mapping (no AutoMapper, by design).</summary>
public static class Mappers
{
    public static LinkResponse ToResponse(this Link link) => new(
        link.Id, link.CanonicalUrl, link.Host?.Hostname ?? string.Empty, link.Title, link.Description,
        link.ThumbnailUrl, link.SiteName, link.EnrichedAt != null, link.Nsfw);

    public static PlaylistResponse ToResponse(
        this Playlist playlist, int itemCount, IEnumerable<string> tags, bool nsfw,
        Guid? folderId = null, string? folderName = null) => new(
        playlist.Id, playlist.Name, playlist.Slug, playlist.Description,
        playlist.Visibility, itemCount, playlist.CreationTime, tags.ToArray(), nsfw, folderId, folderName);

    public static ApiKeyResponse ToResponse(this ApiKey key) => new(
        key.Id, key.Name, key.Prefix, key.Scopes, key.CreationTime, key.LastUsedAt, key.ExpiresAt);
}
