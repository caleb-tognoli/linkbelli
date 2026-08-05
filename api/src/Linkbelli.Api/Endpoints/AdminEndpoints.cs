using Linkbelli.Application.Auth;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Api.Endpoints;

/// <summary>Admin-only endpoints. Require the Admin role and the interactive bearer scheme.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = IdentityConstants.BearerScheme,
                Roles = AppRoles.Admin,
            })
            .WithTags("Admin");

        // User lookup (search by username/email) → resolves the id for quota management.
        group.MapGet("/users", async (IAdminService admin, string? q, int? limit, CancellationToken ct) =>
            Results.Ok(await admin.SearchUsersAsync(q, limit, ct)));

        group.MapGet("/users/{userId:guid}/quota", async (Guid userId, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.GetStatusAsync(userId, ct)));

        group.MapPut("/users/{userId:guid}/quota", async (
            Guid userId, SetQuotaRequest req, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.SetAsync(userId, req.MaxSources, req.MaxRunsPerDay, req.MaxItemsPerRun, ct)));

        // Host moderation blocklist.
        group.MapGet("/hosts", async (IAdminService admin, string? q, bool? blocked, int? limit, CancellationToken ct) =>
            Results.Ok(await admin.ListHostsAsync(q, blocked, limit, ct)));

        group.MapPut("/hosts", async (SetHostBlockedRequest req, IAdminService admin, CancellationToken ct) =>
            Results.Ok(await admin.SetHostBlockedAsync(req.Hostname, req.Blocked, ct)));

        // Bulk re-enqueue links for enrichment. Handy after fixing an enricher bug or clearing a
        // 429 wave: pass onlyFailed=true (default) to target only links whose Metadata carries
        // an enrichmentError stamp; add host= to further narrow to one origin (e.g. themoviedb.org).
        group.MapPost("/links/re-enrich", async (
            IAppDbContext db,
            ILinkEnrichmentQueue queue,
            string? host,
            bool? onlyFailed,
            CancellationToken ct) =>
        {
            var query = db.Links.AsQueryable();
            if (onlyFailed ?? true)
            {
                // Metadata is jsonb — string.Contains isn't translatable, but Npgsql's
                // JsonExists checks whether the failure stamp's top-level key is present.
                query = query.Where(l => l.Metadata != null && EF.Functions.JsonExists(l.Metadata, "enrichmentError"));
            }

            if (!string.IsNullOrWhiteSpace(host))
            {
                var hostname = host.Trim();
                query = query.Where(l => l.Host!.Hostname == hostname);
            }

            var ids = await query.Select(l => l.Id).ToListAsync(ct);
            foreach (var id in ids)
            {
                queue.Enqueue(id);
            }

            return Results.Ok(new { queued = ids.Count });
        });
    }
}
