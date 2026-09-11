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
            int? limit, string? cursor, string? sort, string? source, string? status, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), playlistId, limit, cursor, sort, source, status, q, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        items.MapPost("/", async (Guid playlistId, AddItemRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
        {
            var created = await svc.AddAsync(user.GetUserId(), playlistId, req, ct);
            return Results.Created($"{ApiRoutes.V1}/items/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        // A chat log, a list of tabs, an email. Adding links one at a time or exporting a file to
        // import it were the only two ways in.
        items.MapPost("/paste", async (Guid playlistId, PasteRequest req, ClaimsPrincipal user,
            IPasteService svc, CancellationToken ct) =>
            Results.Ok(await svc.PasteAsync(user.GetUserId(), playlistId, req.Text, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("PasteLinks");

        var item = app.MapGroup("/items").RequireAuthorization(secured).WithTags("Playlist items");

        // One action over a selection. Everything here exists per item already; this is doing it
        // to forty of them without forty round trips.
        item.MapPost("/bulk", async (BulkItemRequest req, ClaimsPrincipal user,
            IBulkItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.ApplyAsync(user.GetUserId(), req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

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

        // One link, with the note that came with it. Sharing used to mean making a whole playlist
        // public, or sending a bare address and losing the reason for sending it.
        item.MapPost("/{id:guid}/share", async (
            Guid id, ClaimsPrincipal user, IItemShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.ShareAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("ShareItem");

        item.MapDelete("/{id:guid}/share", async (
            Guid id, ClaimsPrincipal user, IItemShareService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RevokeItemShare");

        item.MapPost("/{id:guid}/move", async (Guid id, MoveItemRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        item.MapPut("/{id:guid}/score", async (Guid id, SetScoreRequest req, ClaimsPrincipal user,
            IPlaylistItemService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetScoreAsync(user.GetUserId(), id, req.Score, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));
    }
}
