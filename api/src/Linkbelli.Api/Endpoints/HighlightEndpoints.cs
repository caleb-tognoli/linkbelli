using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Passages marked in saved articles.
/// </summary>
/// <remarks>
/// Hung off <c>/links</c> rather than off a playlist item, because a link in two playlists is one
/// article: the reader already unions reading progress across copies, and a highlight that existed
/// in one list but not the other would be the same passage marked twice.
/// </remarks>
public static class HighlightEndpoints
{
    public static void MapHighlightEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/links")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Highlights");

        links.MapGet("/{id:guid}/highlights", async (
            Guid id, ClaimsPrincipal user, IHighlightService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListForLinkAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListHighlightsForLink");

        links.MapPost("/{id:guid}/highlights", async (
            Guid id,
            CreateHighlightRequest request,
            ClaimsPrincipal user,
            IHighlightService svc,
            CancellationToken ct) =>
        {
            var highlight = await svc.CreateAsync(user.GetUserId(), id, request, ct);
            return Results.Created($"{ApiRoutes.V1}/highlights/{highlight.Id}", highlight);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("CreateHighlight");

        var group = app.MapGroup("/highlights")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Highlights");

        // Everything somebody has marked, across the whole library. The highest-signal text a
        // person has — it is the part they stopped and chose.
        group.MapGet("/", async (
            string? cursor,
            int? limit,
            ClaimsPrincipal user,
            IHighlightService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), cursor, limit, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListHighlights");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateHighlightRequest request,
            ClaimsPrincipal user,
            IHighlightService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, request, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("UpdateHighlight");

        group.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IHighlightService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("DeleteHighlight");
    }
}
