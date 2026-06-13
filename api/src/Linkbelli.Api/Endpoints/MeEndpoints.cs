using System.Security.Claims;
using Linkbelli.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>Identity info for the caller. Accepts either a bearer token or an API key.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            authMethod = user.FindFirstValue("auth_method") ?? "bearer",
            scopes = user.FindAll("scope").Select(c => c.Value).ToArray(),
        }))
        .RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = $"{IdentityConstants.BearerScheme},{ApiKeyAuthenticationDefaults.Scheme}",
        })
        .WithName("GetMe");
    }
}
