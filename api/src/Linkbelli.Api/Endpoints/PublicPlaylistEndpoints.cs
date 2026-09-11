using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Feeds;
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
            ClaimsPrincipal user, string username, string slug, IPlaylistItemService svc, int? limit, string? cursor, string? sort, string? source, string? status, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAsync(username, slug, limit, cursor, sort, source, status, q, ViewerId(user), ct)))
            .AllowAnonymous();

        // Shared sources attached to a public playlist (private sources are never exposed).
        group.MapGet("/playlists/{username}/{slug}/sources", async (string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAttachedSourcesAsync(username, slug, ct)))
            .AllowAnonymous();

        // Syndication: the same playlist as RSS, Atom or JSON Feed. Linkbelli reads all three
        // already; this is the direction that was missing.
        group.MapGet("/playlists/{username}/{slug}/feed.{format}", async (
            string username, string slug, string format, HttpContext http,
            IPlaylistFeedService feeds, CancellationToken ct) =>
        {
            var parsed = FeedSerializer.Parse(format);
            if (parsed is null)
            {
                return Results.NotFound();
            }

            // Both URLs are built from the public origin, never from the incoming request: behind
            // the web BFF that request arrives as http://api:8080, which is unreachable for a
            // reader and leaks the internal topology into every feed.
            var pageUrl = PublicPageUrl(http, username, slug);
            var selfUrl = $"{pageUrl}/feed.{Uri.EscapeDataString(format)}";

            var feed = await feeds.BuildAsync(username, slug, selfUrl, pageUrl, ct);

            return Results.Text(
                FeedSerializer.Serialize(feed, parsed.Value),
                FeedSerializer.ContentType(parsed.Value));
        })
            .AllowAnonymous()
            .ExcludeFromDescription();

        // A public playlist names its owner; without these there was nowhere to click through to.
        group.MapGet("/users/{username}", async (
            ClaimsPrincipal user, string username, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicProfileAsync(username, ViewerId(user), ct)))
            .AllowAnonymous();

        group.MapGet("/users/{username}/playlists", async (
            ClaimsPrincipal user, string username, IPlaylistService svc,
            int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListUserPublicPlaylistsAsync(username, limit, cursor, ViewerId(user), ct)))
            .AllowAnonymous();

        // Public tag cloud: tags used across public playlists, with counts.
        group.MapGet("/tags", async (IPlaylistService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicTagsAsync(q, ct)))
            .AllowAnonymous();
    }

    /// <summary>
    /// Where a human reads this playlist. Configure "PublicWebBaseUrl" with the web app's origin
    /// so feeds link people to the real page instead of back at the API.
    /// </summary>
    private static string PublicPageUrl(HttpContext http, string username, string slug)
    {
        var configured = http.RequestServices.GetRequiredService<IConfiguration>()["PublicWebBaseUrl"];
        var origin = string.IsNullOrWhiteSpace(configured)
            ? $"{http.Request.Scheme}://{http.Request.Host}"
            : configured.TrimEnd('/');

        return $"{origin}/public/{Uri.EscapeDataString(username)}/{Uri.EscapeDataString(slug)}";
    }

    /// <summary>The authenticated viewer's id, or null for anonymous callers.</summary>
    private static Guid? ViewerId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.GetUserId() : null;
}
