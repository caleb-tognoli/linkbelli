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
            // Roles, so a client can offer the admin console to the people it will work for
            // rather than showing everyone a link that 403s.
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            showNsfw = await prefs.ShowNsfwAsync(user.GetUserId(), ct),
            archiveLinks = await prefs.ArchiveLinksAsync(user.GetUserId(), ct),
            backupsEnabled = await prefs.BackupsEnabledAsync(user.GetUserId(), ct),
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

        // Playlists other people share with the caller. They are not in the caller's own list,
        // which is theirs — but they still have to be findable.
        app.MapGet("/me/shared", async (ClaimsPrincipal user, IPlaylistMemberService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSharedWithMeAsync(user.GetUserId(), ct)))
        .RequireAuthorization(secured)
        .WithName("ListSharedWithMe");

        app.MapPut("/me/preferences", async (UpdatePreferencesRequest req, ClaimsPrincipal user, IUserPreferenceService prefs, CancellationToken ct) =>
        {
            if (req.ShowNsfw is { } nsfw)
            {
                await prefs.SetShowNsfwAsync(user.GetUserId(), nsfw, ct);
            }

            if (req.ArchiveLinks is { } archive)
            {
                await prefs.SetArchiveLinksAsync(user.GetUserId(), archive, ct);
            }

            if (req.BackupsEnabled is { } backups)
            {
                await prefs.SetBackupsEnabledAsync(user.GetUserId(), backups, ct);
            }

            return Results.NoContent();
        })
        .RequireAuthorization(secured)
        .WithName("UpdateMyPreferences");
    }
}
