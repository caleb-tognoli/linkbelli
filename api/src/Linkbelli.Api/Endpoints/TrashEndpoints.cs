using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Recovery for things the caller deleted. Deletion has always been soft — this is the way back
/// to those rows, and the way to clear them out early. Owner-scoped throughout.
/// </summary>
public static class TrashEndpoints
{
    public static void MapTrashEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/trash")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Trash");

        group.MapGet("/", async (ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("GetTrash");

        group.MapDelete("/", async (ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
        {
            await svc.EmptyAsync(user.GetUserId(), ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("EmptyTrash");

        group.MapPost("/playlists/{id:guid}/restore", async (
            Guid id, ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
        {
            await svc.RestorePlaylistAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RestorePlaylist");

        group.MapPost("/items/{id:guid}/restore", async (
            Guid id, ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
        {
            await svc.RestoreItemAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RestoreItem");

        group.MapDelete("/playlists/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
        {
            await svc.PurgePlaylistAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("PurgeTrashedPlaylist");

        group.MapDelete("/items/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ITrashService svc, CancellationToken ct) =>
        {
            await svc.PurgeItemAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("PurgeTrashedItem");
    }
}
