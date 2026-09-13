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

        group.MapGet("/", async (ClaimsPrincipal user, IPlaylistService svc, int? limit, string? cursor, string[]? tag, string? q, bool? unfiled, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), limit, cursor, tag, q, unfiled ?? false, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        // How the caller likes to look at this playlist — sort, filters, what the rows show.
        // Saved per account rather than per browser, so it follows them between devices.
        group.MapPut("/{id:guid}/view", async (
            Guid id, PlaylistViewPreferences req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            await svc.SaveViewAsync(user.GetUserId(), id, req, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapPost("/", async (CreatePlaylistRequest req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/playlists/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        // Sharing was all-or-nothing public: showing one list to one person meant publishing it
        // to everyone, and collaborating on one meant handing over an account.
        group.MapGet("/{id:guid}/members", async (
            Guid id, ClaimsPrincipal user, IPlaylistMemberService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListPlaylistMembers");

        group.MapPut("/{id:guid}/members/{username}", async (
            Guid id, string username, SetPlaylistMemberRequest req, ClaimsPrincipal user,
            IPlaylistMemberService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetAsync(user.GetUserId(), id, username, req.Role, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("SetPlaylistMember");

        group.MapDelete("/{id:guid}/members/{username}", async (
            Guid id, string username, ClaimsPrincipal user, IPlaylistMemberService svc, CancellationToken ct) =>
        {
            await svc.RemoveAsync(user.GetUserId(), id, username, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RemovePlaylistMember");

        // Membership resolved an existing username or failed, so collaborating meant the other
        // person already had an account here and you knew their exact username — which, on an
        // instance you have just stood up, nobody does.
        group.MapPost("/{id:guid}/invites", async (
            Guid id, CreateInviteRequest req, ClaimsPrincipal user,
            IInviteService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .RequireRateLimiting("sensitive")
            .WithName("CreatePlaylistInvite");

        group.MapGet("/{id:guid}/invites", async (
            Guid id, ClaimsPrincipal user, IInviteService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListPlaylistInvites");

        group.MapDelete("/{id:guid}/invites/{inviteId:guid}", async (
            Guid id, Guid inviteId, ClaimsPrincipal user, IInviteService svc, CancellationToken ct) =>
        {
            await svc.RevokeAsync(user.GetUserId(), inviteId, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RevokePlaylistInvite");

        // The lightest thing a visitor can say about someone else's list. Signed in, because a
        // count anyone can run up says nothing — and it is what discovery ranks on.
        group.MapPost("/{id:guid}/like", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.LikeAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("LikePlaylist");

        group.MapDelete("/{id:guid}/like", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnlikeAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("UnlikePlaylist");

        group.MapPatch("/{id:guid}", async (Guid id, UpdatePlaylistRequest req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        // Sources currently feeding this playlist (incl. cross-user shared subscriptions).
        group.MapGet("/{id:guid}/sources", async (Guid id, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAttachedSourcesAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead));

        // Subscribe/unsubscribe a source to this playlist (own source, or any shared one).
        group.MapPost("/{id:guid}/sources", async (Guid id, SubscribeSourceRequest req, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            var discoveredCount = await svc.SubscribeSourceAsync(user.GetUserId(), id, req.SourceId, ct);
            return Results.Ok(new { discoveredCount });
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapPost("/{id:guid}/sources/{sourceId:guid}/backfill", async (Guid id, Guid sourceId, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            var itemsAdded = await svc.BackfillFromSourceAsync(user.GetUserId(), id, sourceId, ct);
            return Results.Ok(new { itemsAdded });
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));

        group.MapDelete("/{id:guid}/sources/{sourceId:guid}", async (Guid id, Guid sourceId, ClaimsPrincipal user, IPlaylistService svc, CancellationToken ct) =>
        {
            await svc.UnsubscribeSourceAsync(user.GetUserId(), id, sourceId, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite));
    }
}
