using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Feeds;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

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
        group.MapGet("/playlists", async (ClaimsPrincipal user, IDiscoveryService svc, string? q, string[]? tag, string? sort, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.DiscoverPublicAsync(q, tag, sort, limit, cursor, ViewerId(user), ct)))
            .AllowAnonymous();

        // What the sitemap is built from. Its own endpoint because rendering it from the
        // discovery listing meant fifty serial round trips per crawler fetch, each running a
        // deeper offset query carrying five correlated subqueries per row — none of which a
        // crawler reads. This is one query of three columns, five thousand rows at a time.
        group.MapGet("/sitemap", async (IDiscoveryService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListForSitemapAsync(limit, cursor, ct)))
            .AllowAnonymous()
            .WithName("ListForSitemap");

        // Take a copy. Discovery exists to put a list you want in front of you, and the only
        // things you could do with one were follow it — a stream of what it gains next, not the
        // thing you just found — or copy the links one at a time.
        //
        // Signed in, obviously, and rate-limited: this writes a row per item on one request.
        group.MapPost("/playlists/{username}/{slug}/fork", async (
            ClaimsPrincipal user, string username, string slug, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.ForkAsync(user.GetUserId(), username, slug, ct)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .RequireRateLimiting("sensitive")
            .WithName("ForkPlaylist");

        // Lists like this one. Discovery otherwise ends at whatever you happened to open — there
        // was no way from a playlist you liked to the next one.
        group.MapGet("/playlists/{username}/{slug}/similar", async (
            ClaimsPrincipal user, string username, string slug, IDiscoveryService svc, int? limit,
            CancellationToken ct) =>
            Results.Ok(await svc.ListSimilarAsync(username, slug, limit, ViewerId(user), ct)))
            .AllowAnonymous()
            .WithName("ListSimilarPlaylists");

        // What a share link opens. The token is the whole secret, so this is deliberately not
        // enumerable and says nothing about the playlist the item came from.
        group.MapGet("/items/{token}", async (string token, IItemShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(token, ct)))
            .AllowAnonymous()
            .WithName("GetSharedItem");

        group.MapGet("/playlists/{username}/{slug}", async (ClaimsPrincipal user, string username, string slug, IPublicPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicAsync(username, slug, ViewerId(user), ct)))
            .AllowAnonymous();

        group.MapGet("/playlists/{username}/{slug}/items", async (
            ClaimsPrincipal user, string username, string slug, IPlaylistItemService svc, int? limit, string? cursor, string? sort, string? source, string? status, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicAsync(username, slug, limit, cursor, sort, source, status, q, ViewerId(user), ct)))
            .AllowAnonymous();

        // Shared sources attached to a public playlist (private sources are never exposed).
        group.MapGet("/playlists/{username}/{slug}/sources", async (string username, string slug, IPlaylistSourceService svc, CancellationToken ct) =>
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
            ClaimsPrincipal user, string username, IPublicPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetPublicProfileAsync(username, ViewerId(user), ct)))
            .AllowAnonymous();

        group.MapGet("/users/{username}/playlists", async (
            ClaimsPrincipal user, string username, IPublicPlaylistService svc,
            int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListUserPublicPlaylistsAsync(username, limit, cursor, ViewerId(user), ct)))
            .AllowAnonymous();

        // Telling whoever runs this instance that something published here is wrong. Signed in,
        // because a queue anyone can fill anonymously is a queue nobody reads.
        group.MapPost("/playlists/{username}/{slug}/report", async (
            string username, string slug, CreateReportRequest req, ClaimsPrincipal user,
            IContentReportService svc, CancellationToken ct) =>
            Results.Ok(await svc.ReportAsync(user.GetUserId(), username, slug, req.Reason, req.Note, ct)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .RequireRateLimiting("sensitive")
            .WithName("ReportPlaylist");

        // Public tag cloud: tags used across public playlists, with counts.
        group.MapGet("/tags", async (IDiscoveryService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListPublicTagsAsync(q, ct)))
            .AllowAnonymous();

        // The same cloud, narrowed to what has actually seen activity. The all-time one is
        // dominated by whatever was popular first and never changes.
        group.MapGet("/tags/trending", async (IDiscoveryService svc, int? days, CancellationToken ct) =>
            Results.Ok(await svc.ListTrendingTagsAsync(days, ct)))
            .AllowAnonymous()
            .WithName("ListTrendingTags");
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
