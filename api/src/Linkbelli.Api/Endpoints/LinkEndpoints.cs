using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/links")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Links");

        // Create-only: canonicalizes and globally deduplicates, returning the resolved link.
        group.MapPost("/", async (CreateLinkRequest req, ILinkService links, CancellationToken ct) =>
        {
            var link = await links.CreateAsync(req, ct);
            return Results.Created($"{ApiRoutes.V1}/links/{link.Id}", link);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.LinksWrite));

        // Preview metadata for a URL without saving (paste → preview → confirm).
        // Live outbound fetch, so rate-limit it like other outbound endpoints.
        group.MapPost("/preview", async (CreateLinkRequest req, ILinkService links, CancellationToken ct) =>
            Results.Ok(await links.PreviewAsync(req.Url, ct)))
            .RequireRateLimiting("sensitive")
            .RequireAuthorization(Scopes.Policy(Scopes.LinksWrite));
    }
}
