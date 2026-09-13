using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// The far side of an invitation link.
/// </summary>
/// <remarks>
/// Separate from the playlist group because the person opening one is, by assumption, not yet
/// anybody here. The preview is anonymous so they can see what they are being asked to join
/// before deciding whether to make an account; accepting is not, because it needs somebody to
/// make a member of.
/// </remarks>
public static class InviteEndpoints
{
    public static void MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/invites").WithTags("Invites");

        group.MapGet("/{token}", async (string token, IInviteService svc, CancellationToken ct) =>
            Results.Ok(await svc.PreviewAsync(token, ct)))
            .AllowAnonymous()
            .RequireRateLimiting("sensitive")
            .WithName("PreviewInvite");

        group.MapPost("/{token}/accept", async (
            string token, ClaimsPrincipal user, IInviteService svc, CancellationToken ct) =>
            Results.Ok(await svc.AcceptAsync(user.GetUserId(), token, ct)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .RequireRateLimiting("sensitive")
            .WithName("AcceptInvite");
    }
}
