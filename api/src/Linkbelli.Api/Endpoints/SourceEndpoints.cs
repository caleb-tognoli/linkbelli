using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sources")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Sources");

        group.MapGet("/", async (ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesRead));

        // Browse shared sources (any owner) so they can be subscribed to a playlist.
        group.MapGet("/shared", async (ISourceService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListSharedAsync(q, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesRead));

        group.MapPost("/", async (CreateSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/sources/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesWrite));

        // Dry-run a config (live outbound fetch) — rate-limited stricter than ordinary reads.
        group.MapPost("/preview", async (PreviewSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.PreviewAsync(user.GetUserId(), req, ct)))
            .RequireRateLimiting("sensitive")
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesWrite));

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesRead));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesWrite));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesWrite));

        group.MapPost("/{id:guid}/run", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            await svc.RunNowAsync(user.GetUserId(), id, ct);
            return Results.Accepted();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesWrite));

        group.MapGet("/{id:guid}/runs", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRunsAsync(user.GetUserId(), id, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.SourcesRead));
    }
}
