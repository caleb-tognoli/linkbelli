using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Webhooks;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Outbound webhooks: addresses Linkbelli tells about what happens in a library.
/// </summary>
/// <remarks>
/// Signed-in sessions only, like API key management. A webhook forwards everything that happens
/// from here on to an address of the caller's choosing, so an API key limited to reading one
/// thing must not be able to set one up and quietly widen itself into a copy of everything.
/// </remarks>
public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me/webhooks")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = IdentityConstants.BearerScheme })
            .WithTags("Webhooks");

        group.MapGet("/events", (IWebhookService svc) => Results.Ok(svc.Events()))
            .WithName("ListWebhookEvents");

        group.MapGet("/", async (ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .WithName("ListWebhooks");

        group.MapPost("/", async (
            CreateWebhookRequest request, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), request, ct);
            return Results.Created($"{ApiRoutes.V1}/me/webhooks/{created.Webhook.Id}", created);
        })
            .WithName("CreateWebhook");

        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateWebhookRequest request, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, request, ct)))
            .WithName("UpdateWebhook");

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .WithName("DeleteWebhook");

        group.MapPost("/{id:guid}/secret", async (
            Guid id, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.RotateSecretAsync(user.GetUserId(), id, ct)))
            .WithName("RotateWebhookSecret");

        // Outbound, to an address of the caller's choosing, on demand — rate-limited like every
        // other endpoint that makes this server fetch something because somebody asked.
        group.MapPost("/{id:guid}/test", async (
            Guid id, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Accepted(value: await svc.TestAsync(user.GetUserId(), id, ct)))
            .RequireRateLimiting("sensitive")
            .WithName("TestWebhook");

        group.MapGet("/{id:guid}/deliveries", async (
            Guid id, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListDeliveriesAsync(user.GetUserId(), id, ct)))
            .WithName("ListWebhookDeliveries");

        group.MapPost("/deliveries/{deliveryId:guid}/redeliver", async (
            Guid deliveryId, ClaimsPrincipal user, IWebhookService svc, CancellationToken ct) =>
            Results.Accepted(value: await svc.RedeliverAsync(user.GetUserId(), deliveryId, ct)))
            .RequireRateLimiting("sensitive")
            .WithName("RedeliverWebhook");
    }
}
