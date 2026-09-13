using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Email;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Linkbelli.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>Caller identity, quota, and preferences. Accepts either a bearer token or an API key.</summary>
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        app.MapGet("/me", async (ClaimsPrincipal user, IUserPreferenceService prefs, IOptions<EmailOptions> email, CancellationToken ct) =>
        {
            // One read. This was four queries against the same row, on the request the web app's
            // layout makes for every server-rendered navigation.
            var preferences = await prefs.GetAsync(user.GetUserId(), ct);

            return Results.Ok(new
            {
                userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                username = user.FindFirstValue(ClaimTypes.Name),
                email = user.FindFirstValue(ClaimTypes.Email),
                authMethod = user.FindFirstValue("auth_method") ?? "bearer",
                scopes = user.FindAll("scope").Select(c => c.Value).ToArray(),
                // Roles, so a client can offer the admin console to the people it will work for
                // rather than showing everyone a link that 403s.
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
                preferences.ShowNsfw,
                preferences.ArchiveLinks,
                preferences.BackupsEnabled,
                preferences.OnboardingDismissed,
                // So a screen that promises mail can say why none is arriving, rather than
                // leaving somebody to conclude the feature is broken.
                preferences.EmailConfirmed,
                // So the sources page can show an inbox address, or say nothing when this
                // deployment has no inbound domain rather than offering one that goes nowhere.
                inboxDomain = email.Value.InboxDomain,
            });
        })
        .RequireAuthorization(secured)
        .WithName("GetMe");

        // The counterpart of the four export formats above. Somebody who wanted to leave had no
        // route at all: their account, their public profile and their sitemap entries stayed up
        // forever, and the operator could not remove them either.
        app.MapDelete("/me", async (
            ClaimsPrincipal user,
            // Explicit: minimal APIs refuse to infer a body on DELETE, and this one carries the
            // password that makes the action deliberate.
            [Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest request,
            IAccountDeletionService deletion,
            CancellationToken ct) =>
        {
            if (string.IsNullOrEmpty(request.Password))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = ["Confirm with your password."],
                });
            }

            var at = await deletion.RequestAsync(user.GetUserId(), request.Password, ct);
            return Results.Ok(new AccountDeletionScheduled(at));
        })
        .RequireAuthorization(secured)
        .RequireRateLimiting("sensitive")
        .WithName("DeleteMyAccount");

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

            // One-way: there is no request that brings the checklist back, because nobody has
            // ever wanted that and a false here would otherwise silently undo a dismissal.
            if (req.DismissOnboarding is true)
            {
                await prefs.DismissOnboardingAsync(user.GetUserId(), ct);
            }

            return Results.NoContent();
        })
        .RequireAuthorization(secured)
        .WithName("UpdateMyPreferences");
    }
}
