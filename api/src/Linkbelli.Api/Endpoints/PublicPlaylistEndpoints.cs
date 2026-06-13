using Linkbelli.Application.Services;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Anonymous read access to non-private playlists, addressed by owner username + slug.
/// Private playlists return 404 (indistinguishable from non-existent).
/// </summary>
public static class PublicPlaylistEndpoints
{
    public static void MapPublicPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public/playlists").WithTags("Public");

        group.MapGet("/{username}/{slug}", async (string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicAsync(username, slug, ct)))
            .AllowAnonymous();

        group.MapGet("/{username}/{slug}/items", async (
            string username, string slug, IPlaylistItemService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAsync(username, slug, limit, cursor, ct)))
            .AllowAnonymous();
    }
}
