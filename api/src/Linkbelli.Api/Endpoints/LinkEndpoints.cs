using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/links")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Links");

        // Create-only: canonicalizes and globally deduplicates, returning the resolved link.
        group.MapPost("/", async (CreateLinkRequest req, ILinkService links, CancellationToken ct) =>
        {
            var link = await links.CreateAsync(req, ct);
            return Results.Created($"{ApiRoutes.V1}/links/{link.Id}", link);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.LinksWrite));

        // Preview metadata for a URL without saving (paste → preview → confirm).
        // Live outbound fetch, so rate-limit it like other outbound endpoints.
        group.MapPost("/preview", async (CreateLinkRequest req, ILinkService links, CancellationToken ct) =>
            Results.Ok(await links.PreviewAsync(req.Url, ct)))
            .RequireRateLimiting("sensitive")
            .RequireAuthorization(Scopes.Policy(Scopes.LinksWrite));

        // The article as it was when it was saved. The web copy is the part that rots, so this is
        // the only version that is still guaranteed to be there.
        group.MapGet("/{id:guid}/content", async (
            Guid id, System.Security.Claims.ClaimsPrincipal user, ILinkService links, CancellationToken ct) =>
            Results.Ok(await links.GetContentAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("GetLinkContent");

        // How far down the reader got. Status had two values, so a twenty-two-minute piece read
        // half of on the train was indistinguishable from one never opened — "Up next" kept
        // offering it from the top, and the only way to clear it was to lie by marking it
        // watched. Reaching the end marks it finished, which is the step this removes.
        group.MapPut("/{id:guid}/progress", async (
            Guid id,
            ReadProgressRequest request,
            System.Security.Claims.ClaimsPrincipal user,
            ILinkService links,
            CancellationToken ct) =>
        {
            await links.SetReadProgressAsync(user.GetUserId(), id, request.Progress, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("SetReadProgress");

        // Thumbnails are served from here rather than hotlinked. Rendering the origin URL told
        // every site in a playlist the viewer's IP and what they were looking at, and broke
        // outright whenever a host refused hotlinking.
        //
        // Anonymous, because thumbnails appear on public playlist pages. On its own rate limit
        // rather than the strict one: a page asks for one of these per row, and a miss — the only
        // case that costs an outbound fetch — is bounded inside the cache instead.
        app.MapGet("/thumbnails/{id:guid}", async (
            Guid id, IThumbnailCache cache, HttpContext http, CancellationToken ct) =>
        {
            var thumbnail = await cache.GetAsync(id, ct);
            if (thumbnail is null)
            {
                // The page falls back to the site's favicon, exactly as it does for a link that
                // never had an image.
                return Results.NotFound();
            }

            // Immutable for a day: a thumbnail for a given link effectively never changes, and
            // this is the request a playlist page makes dozens of at a time.
            http.Response.Headers.CacheControl = "public, max-age=86400";

            return Results.File(thumbnail.Content, thumbnail.ContentType);
        })
            .AllowAnonymous()
            .RequireRateLimiting("thumbnails")
            .WithTags("Links")
            .WithName("GetThumbnail");

        // Try a link again now. Links are global, so this is deliberately not owner-scoped:
        // any signed-in caller who can see a failed link can ask for it to be re-fetched, and
        // the outbound rate limit is what stops that being abused.
        group.MapPost("/{id:guid}/recheck", async (
            Guid id, ILinkRecheckService recheck, CancellationToken ct) =>
        {
            await recheck.RecheckAsync(id, ct);
            return Results.Accepted();
        })
            .RequireRateLimiting("sensitive")
            .RequireAuthorization(Scopes.Policy(Scopes.LinksWrite));
    }
}
