using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// What changed since a client last looked. Without it a caching client has only two options:
/// re-read everything, or trust a stale copy.
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sync", async (
            ClaimsPrincipal user, ISyncService svc, DateTimeOffset? since, CancellationToken ct) =>
            Results.Ok(await svc.ChangesAsync(user.GetUserId(), since, ct)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Sync")
            .WithName("Sync");
    }
}
