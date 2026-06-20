using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        app.MapPost("/import", async (ImportRequest req, ClaimsPrincipal user, IImportService svc, CancellationToken ct) =>
            Results.Ok(await svc.ImportAsync(user.GetUserId(), req, ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithTags("Import");
    }
}
