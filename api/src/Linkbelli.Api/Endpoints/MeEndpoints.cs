using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>Caller identity + quota. Accepts either a bearer token or an API key.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            authMethod = user.FindFirstValue("auth_method") ?? "bearer",
            scopes = user.FindAll("scope").Select(c => c.Value).ToArray(),
        }))
        .RequireAuthorization(secured)
        .WithName("GetMe");

        app.MapGet("/me/quota", async (ClaimsPrincipal user, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.GetStatusAsync(user.GetUserId(), ct)))
        .RequireAuthorization(secured)
        .WithName("GetMyQuota");
    }
}
