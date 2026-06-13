using System.Security.Claims;
using Linkbelli.Core.Auth;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.BearerScheme });

        group.MapGet("/", async (ClaimsPrincipal user, LinkbelliDbContext db) =>
        {
            var userId = GetUserId(user);
            var keys = await db.ApiKeys
                .Where(k => k.UserId == userId)
                .OrderByDescending(k => k.CreationTime)
                .Select(k => new ApiKeyResponse(
                    k.Id, k.Name, k.Prefix, k.Scopes, k.CreationTime, k.LastUsedAt, k.ExpiresAt))
                .ToListAsync();
            return Results.Ok(keys);
        });

        group.MapPost("/", async (CreateApiKeyRequest req, ClaimsPrincipal user, LinkbelliDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["name"] = ["Name is required."],
                });
            }

            var generated = ApiKeyToken.Generate();
            var key = new ApiKey
            {
                UserId = GetUserId(user),
                Name = req.Name.Trim(),
                Prefix = generated.PublicId,
                Hash = generated.Hash,
                Scopes = req.Scopes ?? [],
                ExpiresAt = req.ExpiresAt,
            };
            db.ApiKeys.Add(key);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/me/apikeys/{key.Id}",
                new CreateApiKeyResponse(key.Id, key.Name, key.Prefix, generated.Token, key.Scopes, key.ExpiresAt));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, LinkbelliDbContext db) =>
        {
            var userId = GetUserId(user);
            var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
            if (key is null)
            {
                return Results.NotFound();
            }

            db.ApiKeys.Remove(key); // soft delete via SaveChanges override = revocation
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Guid GetUserId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
