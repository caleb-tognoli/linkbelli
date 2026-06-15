using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// The private folder system for organizing playlists. Every route is owner-scoped; folders
/// are never exposed publicly. Reuses the playlists read/write scopes since folders are purely
/// a way to arrange the caller's own and saved playlists.
/// </summary>
public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/folders")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Folders");

        group.MapGet("/", async (ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        group.MapPost("/", async (CreateFolderRequest req, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/folders/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        group.MapPatch("/{id:guid}", async (Guid id, RenameFolderRequest req, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.RenameAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapPost("/{id:guid}/move", async (Guid id, MoveFolderRequest req, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
            Results.Ok(await svc.MoveAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        // File a playlist (own, any visibility; or any public one) into this folder. Moves it if already filed.
        group.MapPost("/{id:guid}/playlists", async (Guid id, AddFolderPlaylistRequest req, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
        {
            await svc.AddPlaylistAsync(user.GetUserId(), id, req.PlaylistId, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapDelete("/{id:guid}/playlists/{playlistId:guid}", async (Guid id, Guid playlistId, ClaimsPrincipal user, IFolderService svc, CancellationToken ct) =>
        {
            await svc.RemovePlaylistAsync(user.GetUserId(), id, playlistId, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));
    }
}
