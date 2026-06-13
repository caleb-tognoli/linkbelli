using Linkbelli.Application.Services;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Anonymous read surface: discover public playlists, read a non-private playlist by owner
/// username + slug, and browse the public tag cloud. Private playlists return 404
/// (indistinguishable from non-existent).
/// </summary>
public static class PublicPlaylistEndpoints
{
    public static void MapPublicPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public").WithTags("Public");

        // Discover: search/browse public playlists (by name query and/or tag).
        group.MapGet("/playlists", async (IPlaylistService svc, string? q, string? tag, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.DiscoverPublicAsync(q, tag, limit, cursor, ct)))
            .AllowAnonymous();

        group.MapGet("/playlists/{username}/{slug}", async (string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicAsync(username, slug, ct)))
            .AllowAnonymous();

        group.MapGet("/playlists/{username}/{slug}/items", async (
            string username, string slug, IPlaylistItemService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAsync(username, slug, limit, cursor, ct)))
            .AllowAnonymous();

        // Public tag cloud: tags used across public playlists, with counts.
        group.MapGet("/tags", async (IPlaylistService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicTagsAsync(q, ct)))
            .AllowAnonymous();
    }
}
