using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Automation;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Rules over your own collection. Everything a source finds otherwise lands where the source was
/// pointed and stays there, so filing it is a decision made again for every single item.
/// </summary>
public static class AutomationEndpoints
{
    public static void MapAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/automations")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Automation");

        group.MapGet("/", async (ClaimsPrincipal user, IAutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListAutomationRules");

        group.MapPost("/", async (
            CreateAutomationRuleRequest req, ClaimsPrincipal user, IAutomationRuleService svc, CancellationToken ct) =>
        {
            var rule = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/automations/{rule.Id}", rule);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("CreateAutomationRule");

        // What it would have caught among what is already saved. A rule only ever acts on what
        // arrives next, so otherwise the only way to find out whether it works is to wait.
        group.MapPost("/preview", async (
            CreateAutomationRuleRequest req, ClaimsPrincipal user, IAutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.PreviewAsync(user.GetUserId(), req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("PreviewAutomationRule");

        group.MapPatch("/{id:guid}", async (
            Guid id, UpdateAutomationRuleRequest req, ClaimsPrincipal user,
            IAutomationRuleService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("UpdateAutomationRule");

        // Rules only ever saw what arrived after they were written. You write a rule because you
        // noticed a pattern in the library you already have, so this runs it over that.
        group.MapPost("/{id:guid}/run", async (
            Guid id, RunAutomationRuleRequest? req, ClaimsPrincipal user,
            IAutomationRunner runner, CancellationToken ct) =>
        {
            var acted = await runner.ApplyRuleAsync(user.GetUserId(), id, req?.PlaylistId, ct);
            return Results.Ok(new { acted });
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("RunAutomationRule");

        group.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IAutomationRuleService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("DeleteAutomationRule");
    }
}
