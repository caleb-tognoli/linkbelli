using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class PlaylistItemEndpoints
{
    public static void MapPlaylistItemEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        var items = app.MapGroup("/playlists/{playlistId:guid}/items")
            .RequireAuthorization(secured).WithTags("Playlist items");

        items.MapGet("/", async (Guid playlistId, ClaimsPrincipal user, IPlaylistItemService svc,
            int? limit, string? cursor, string? sort, string? source, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), playlistId, limit, cursor, sort, source, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        items.MapPost("/", async (Guid playlistId, AddItemRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
        {
            var created = await svc.AddAsync(user.GetUserId(), playlistId, req, ct);
            return Results.Created($"{ApiRoutes.V1}/items/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        var item = app.MapGroup("/items").RequireAuthorization(secured).WithTags("Playlist items");

        item.MapPatch("/{id:guid}", async (Guid id, UpdateItemRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        item.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IPlaylistItemService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        item.MapPost("/{id:guid}/move", async (Guid id, MoveItemRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));
    }
}
