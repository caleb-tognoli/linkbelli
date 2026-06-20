using System.Security.Claims;
using Linkbelli.Api.Auth;
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

        // Discover: search/browse public playlists (by name query and/or tag). NSFW filtered by
        // the viewer's preference if authenticated, otherwise hidden.
        group.MapGet("/playlists", async (ClaimsPrincipal user, IPlaylistService svc, string? q, string[]? tag, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.DiscoverPublicAsync(q, tag, limit, cursor, ViewerId(user), ct)))
            .AllowAnonymous();

        group.MapGet("/playlists/{username}/{slug}", async (ClaimsPrincipal user, string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicAsync(username, slug, ViewerId(user), ct)))
            .AllowAnonymous();

        group.MapGet("/playlists/{username}/{slug}/items", async (
            ClaimsPrincipal user, string username, string slug, IPlaylistItemService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAsync(username, slug, limit, cursor, ViewerId(user), ct)))
            .AllowAnonymous();

        // Shared sources attached to a public playlist (private sources are never exposed).
        group.MapGet("/playlists/{username}/{slug}/sources", async (string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAttachedSourcesAsync(username, slug, ct)))
            .AllowAnonymous();

        // Public tag cloud: tags used across public playlists, with counts.
        group.MapGet("/tags", async (IPlaylistService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicTagsAsync(q, ct)))
            .AllowAnonymous();
    }

    /// <summary>The authenticated viewer's id, or null for anonymous callers.</summary>
    private static Guid? ViewerId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.GetUserId() : null;
}
