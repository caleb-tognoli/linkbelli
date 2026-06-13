using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>The caller's own tags (across their playlists), with counts — for autocomplete/management.</summary>
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
    }
}
