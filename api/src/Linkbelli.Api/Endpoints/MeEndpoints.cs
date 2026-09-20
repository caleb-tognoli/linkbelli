using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Email;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
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

        // Signed in, there was no way to change a password: the only route was the signed-out
        // reset flow, which this app redirected a signed-in visitor away from. Bearer only —
        // an API key is for programs, and a program has no business changing the password that
        // would revoke it.
        app.MapPost("/me/password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal user,
            UserManager<ApplicationUser> users,
            IAuditLog audit,
            CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrEmpty(request.CurrentPassword))
            {
                errors["currentPassword"] = ["Your current password is required."];
            }

            if (string.IsNullOrEmpty(request.NewPassword))
            {
                errors["newPassword"] = ["A new password is required."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var account = await users.FindByIdAsync(user.GetUserId().ToString());
            if (account is null)
            {
                return Results.NotFound();
            }

            var result = await users.ChangePasswordAsync(
                account, request.CurrentPassword, request.NewPassword);

            if (!result.Succeeded)
            {
                // Told apart deliberately: "that is not your password" and "that password is not
                // allowed" are different problems with different fixes, and one message for both
                // leaves somebody retyping a password that was right.
                var wrong = result.Errors.Any(e => e.Code == "PasswordMismatch");
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [wrong ? "currentPassword" : "newPassword"] = wrong
                        ? ["That password is not right."]
                        : result.Errors.Select(e => e.Description).ToArray(),
                });
            }

            // ChangePasswordAsync rolls the security stamp, so refresh tokens minted for other
            // sessions stop working. Access tokens already issued live out their short lives.
            await audit.RecordAsync(
                account.Id, "user.password_changed", "User", account.Id,
                summary: "Changed their own password", ct: ct);

            return Results.NoContent();
        })
        .RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = IdentityConstants.BearerScheme,
        })
        .RequireRateLimiting("sensitive")
        .WithName("ChangeMyPassword");

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
