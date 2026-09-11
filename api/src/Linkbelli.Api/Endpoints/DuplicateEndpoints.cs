using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// The same thing saved more than once. Dedup stops the identical link landing twice in one
/// playlist and canonicalization strips known tracking parameters — neither helps with the same
/// page across three lists, or reached by an address nobody has taught the canonicalizer about.
/// </summary>
public static class DuplicateEndpoints
{
    public static void MapDuplicateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/duplicates", async (ClaimsPrincipal user, IDuplicateService svc, CancellationToken ct) =>
            Results.Ok(await svc.FindAsync(user.GetUserId(), ct)))
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithTags("Playlists")
            .WithName("FindDuplicates");
    }
}
