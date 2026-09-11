using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Following, and the feed it exists for. Saving a public playlist to a folder files a copy of
/// it; nothing ever told anyone that something new had turned up in it.
/// </summary>
public static class FollowEndpoints
{
    public static void MapFollowEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        app.MapPost("/playlists/{id:guid}/follow", async (
            Guid id, ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
            Results.Ok(await svc.FollowPlaylistAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("FollowPlaylist");

        app.MapDelete("/playlists/{id:guid}/follow", async (
            Guid id, ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnfollowPlaylistAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("UnfollowPlaylist");

        // Following a person means everything they publish, including playlists they haven't
        // made yet — which is the difference between this and following each list by hand.
        app.MapPost("/users/{username}/follow", async (
            string username, ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
            Results.Ok(await svc.FollowUserAsync(user.GetUserId(), username, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("FollowUser");

        app.MapDelete("/users/{username}/follow", async (
            string username, ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnfollowUserAsync(user.GetUserId(), username, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("UnfollowUser");

        app.MapGet("/me/following", async (ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListFollowingAsync(user.GetUserId(), ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("ListFollowing");

        app.MapGet("/feed", async (
            ClaimsPrincipal user, IFollowService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.GetFeedAsync(user.GetUserId(), limit, cursor, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("GetFeed");

        // Explicit rather than a side effect of reading: opening the page and then losing it to a
        // reload should not silently mark everything as seen.
        app.MapPost("/feed/seen", async (ClaimsPrincipal user, IFollowService svc, CancellationToken ct) =>
        {
            await svc.MarkFeedSeenAsync(user.GetUserId(), ct);
            return Results.NoContent();
        })
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Following")
            .WithName("MarkFeedSeen");
    }
}
