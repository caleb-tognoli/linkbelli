using Linkbelli.Api.Auth;
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
            return Results.Created($"/links/{link.Id}", link);
        });
    }
}
