using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Email;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// What Linkbelli will email somebody about, and how to make it stop.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/notifications").WithTags("Notifications");

        var secured = new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey };

        group.MapGet("/", async (ClaimsPrincipal user, INotificationPreferences prefs, CancellationToken ct) =>
            Results.Ok(await prefs.GetAsync(user.GetUserId(), ct)))
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("GetNotificationPreferences");

        group.MapPut("/", async (
            UpdateNotificationsRequest req, ClaimsPrincipal user,
            INotificationPreferences prefs, CancellationToken ct) =>
        {
            await prefs.SetAsync(user.GetUserId(), req, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("UpdateNotificationPreferences");

        group.MapPost("/digest/preview", async (
            ClaimsPrincipal user, IDigestSweep digests, CancellationToken ct) =>
        {
            // Forced, so it arrives even in a week when nothing happened and even if the weekly
            // one is switched off — somebody deciding whether to subscribe should be able to see
            // one rather than wait a week to find out what it looks like.
            var sent = await digests.SendOneAsync(user.GetUserId(), force: true, ct);

            return sent
                ? Results.Accepted()
                : Results.Problem(
                    "Could not send that. This Linkbelli may have no mail configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .RequireAuthorization(secured)
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .RequireRateLimiting("sensitive")
            .WithName("SendDigestPreview");

        // Anonymous, and deliberately. Somebody who does not want mail from us is in the worst
        // position to go and sign in: they may not remember the account at all. Asking them to
        // is how a product earns a spam complaint instead of an unsubscribe.
        group.MapPost("/unsubscribe", async (
            UnsubscribeRequest req, INotificationPreferences prefs, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Token))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["token"] = ["A token is required."],
                });
            }

            var kind = await prefs.UnsubscribeAsync(req.Token, ct);

            return kind is null
                ? Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["token"] = ["That link is not valid. Change it in your profile instead."],
                })
                // Named back, so the page can say what has just been turned off rather than
                // leaving somebody to guess whether it worked.
                : Results.Ok(new { unsubscribed = kind.Value.Slug(), description = kind.Value.Describe() });
        })
            .AllowAnonymous()
            .RequireRateLimiting("sensitive")
            .WithName("Unsubscribe");
    }
}
