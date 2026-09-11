using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>Caller identity, quota, and preferences. Accepts either a bearer token or an API key.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        app.MapGet("/me", async (ClaimsPrincipal user, IUserPreferenceService prefs, CancellationToken ct) => Results.Ok(new
        {
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
            username = user.FindFirstValue(ClaimTypes.Name),
            email = user.FindFirstValue(ClaimTypes.Email),
            authMethod = user.FindFirstValue("auth_method") ?? "bearer",
            scopes = user.FindAll("scope").Select(c => c.Value).ToArray(),
            showNsfw = await prefs.ShowNsfwAsync(user.GetUserId(), ct),
            archiveLinks = await prefs.ArchiveLinksAsync(user.GetUserId(), ct),
        }))
        .RequireAuthorization(secured)
        .WithName("GetMe");

        app.MapGet("/me/quota", async (ClaimsPrincipal user, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.GetStatusAsync(user.GetUserId(), ct)))
        .RequireAuthorization(secured)
        .WithName("GetMyQuota");

        app.MapGet("/me/usage", async (ClaimsPrincipal user, IUsageService usage, CancellationToken ct) =>
            Results.Ok(await usage.GetAsync(user.GetUserId(), ct)))
        .RequireAuthorization(secured)
        .WithName("GetMyUsage");

        app.MapPut("/me/preferences", async (UpdatePreferencesRequest req, ClaimsPrincipal user, IUserPreferenceService prefs, CancellationToken ct) =>
        {
            await prefs.SetShowNsfwAsync(user.GetUserId(), req.ShowNsfw, ct);
            if (req.ArchiveLinks is { } archive)
            {
                await prefs.SetArchiveLinksAsync(user.GetUserId(), archive, ct);
            }

            return Results.NoContent();
        })
        .RequireAuthorization(secured)
        .WithName("UpdateMyPreferences");
    }
}
