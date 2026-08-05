using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class SourceTemplateEndpoints
{
    public static void MapSourceTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/templates")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Templates");

        // Gallery: own + saved + app templates for the current user.
        group.MapGet("/", async (ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)));

        // Admin: all templates regardless of owner / visibility.
        group.MapGet("/all", async (ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAllAsync(ct)))
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });

        // Discover Public/Unlisted templates from other users.
        group.MapGet("/discover", async (
            string? q, string[]? tags,
            ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.DiscoverAsync(user.GetUserId(), q, tags, ct)));

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            var isAdmin = user.IsInRole("Admin");
            return Results.Ok(await svc.GetAsync(id, user.GetUserId(), isAdmin, ct));
        });

        group.MapPost("/", async (CreateSourceTemplateRequest req, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            var isAdmin = user.IsInRole("Admin");
            var created = await svc.CreateAsync(user.GetUserId(), isAdmin, req, ct);
            return Results.Created($"{ApiRoutes.V1}/templates/{created.Id}", created);
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateSourceTemplateRequest req, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            var isAdmin = user.IsInRole("Admin");
            return Results.Ok(await svc.UpdateAsync(id, user.GetUserId(), isAdmin, req, ct));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            var isAdmin = user.IsInRole("Admin");
            await svc.DeleteAsync(id, user.GetUserId(), isAdmin, ct);
            return Results.NoContent();
        });

        // Save / unsave a template (bookmark).
        group.MapPost("/{id:guid}/save", async (Guid id, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            await svc.SaveAsync(id, user.GetUserId(), ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}/save", async (Guid id, ClaimsPrincipal user, ISourceTemplateService svc, CancellationToken ct) =>
        {
            await svc.UnsaveAsync(id, user.GetUserId(), ct);
            return Results.NoContent();
        });
    }
}
