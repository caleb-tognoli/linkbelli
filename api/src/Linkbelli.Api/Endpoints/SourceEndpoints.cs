using System.Security.Claims;
using Linkbelli.Api.Auth;
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
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)));

        group.MapPost("/", async (CreateSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), req, ct);
            return Results.Created($"/sources/{created.Id}", created);
        });

        // Dry-run a config (live outbound fetch) — rate-limited stricter than ordinary reads.
        group.MapPost("/preview", async (PreviewSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.PreviewAsync(user.GetUserId(), req, ct)))
            .RequireRateLimiting("sensitive");

        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(user.GetUserId(), id, ct)));

        group.MapPatch("/{id:guid}", async (Guid id, UpdateSourceRequest req, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.UpdateAsync(user.GetUserId(), id, req, ct)));

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        });

        group.MapPost("/{id:guid}/run", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
        {
            await svc.RunNowAsync(user.GetUserId(), id, ct);
            return Results.Accepted();
        });

        group.MapGet("/{id:guid}/runs", async (Guid id, ClaimsPrincipal user, ISourceService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRunsAsync(user.GetUserId(), id, ct)));
    }
}
