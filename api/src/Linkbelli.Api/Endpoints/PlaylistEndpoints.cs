using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class PlaylistEndpoints
{
    public static void MapPlaylistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/playlists")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Playlists");

        group.MapGet("/", async (ClaimsPrincipal user, IPlaylistService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), limit, cursor, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        group.MapPost("/", async (CreatePlaylistRequest req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/playlists/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        group.MapPatch("/{id:guid}", async (Guid id, UpdatePlaylistRequest req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));
    }
}
