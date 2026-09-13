using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// The caller's own tags, for autocomplete and for tidying up.
/// </summary>
/// <remarks>
/// The name goes in the body rather than the route on both write endpoints. Tag names are free
/// text — normalization lowercases and collapses whitespace but keeps punctuation — so "and/or"
/// is a legal tag, and as a route value that is a path separator the router rejects before the
/// handler ever sees it.
/// </remarks>
public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tags")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Tags");

        group.MapGet("/", async (ClaimsPrincipal user, IPlaylistService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListOwnTagsAsync(user.GetUserId(), q, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        group.MapGet("/usage", async (ClaimsPrincipal user, ITagManagementService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListTagUsage");

        group.MapPost("/rename", async (
            ClaimsPrincipal user,
            [FromBody] RenameTagRequest request,
            ITagManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.RenameAsync(user.GetUserId(), request.From, request.To, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RenameTag");

        group.MapPost("/delete", async (
            ClaimsPrincipal user,
            [FromBody] DeleteTagRequest request,
            ITagManagementService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.RemoveAsync(user.GetUserId(), request.Name, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("DeleteTag");
    }
}
