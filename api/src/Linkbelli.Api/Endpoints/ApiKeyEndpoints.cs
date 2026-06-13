using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Api.Endpoints;

public static class ApiKeyEndpoints
{
    /// <summary>
    /// API key management. Restricted to the interactive bearer scheme: an API
    /// key cannot be used to mint more API keys.
    /// </summary>
    public static void MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/apikeys")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.BearerScheme })
            .WithTags("API keys");

        group.MapGet("/", async (ClaimsPrincipal user, IApiKeyService keys, CancellationToken ct) =>
            Results.Ok(await keys.ListAsync(user.GetUserId(), ct)));

        group.MapPost("/", async (CreateApiKeyRequest req, ClaimsPrincipal user, IApiKeyService keys, CancellationToken ct) =>
        {
            var created = await keys.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"/me/apikeys/{created.Id}", created);
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IApiKeyService keys, CancellationToken ct) =>
        {
            await keys.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        });
    }
}
