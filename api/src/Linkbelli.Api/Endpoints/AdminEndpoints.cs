using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Auth;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Api.Endpoints;

/// <summary>Admin-only endpoints. Require the Admin role and the interactive bearer scheme.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Both schemes now: a key granted an admin scope can reach these, which is what makes
        // running maintenance from a script possible without a person's session token. The Admin
        // role is still required, and a scope cannot grant one.
        var group = app.MapGroup("/admin")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = AuthSchemes.BearerOrApiKey,
                Roles = AppRoles.Admin,
            })
            .WithTags("Admin");

        // Everything worth looking at in one place. All of it was already being recorded and
        // none of it had a view: failing sources, unreadable links, the enrichment backlog.
        group.MapGet("/overview", async (IAdminOverviewService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAsync(ct)))
            .WithName("GetAdminOverview")
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));

        // User lookup (search by username/email) → resolves the id for quota management.
        group.MapGet("/users", async (IAdminService admin, string? q, int? limit, CancellationToken ct) =>
            Results.Ok(await admin.SearchUsersAsync(q, limit, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));

        // An admin could see every user and do nothing about them: no suspend, no delete, no
        // promote or demote. Granting admin meant editing configuration and restarting, so an
        // instance with two admins could not add a third without a deploy.
        group.MapPut("/users/{userId:guid}/suspended", async (
            Guid userId, SetSuspendedRequest req, ClaimsPrincipal user,
            IAdminUserService admin, CancellationToken ct) =>
        {
            if (req.Suspended)
            {
                await admin.SuspendAsync(user.GetUserId(), userId, ct);
            }
            else
            {
                await admin.ReinstateAsync(user.GetUserId(), userId, ct);
            }

            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite))
            .WithName("SetUserSuspended");

        group.MapPut("/users/{userId:guid}/admin", async (
            Guid userId, SetAdminRequest req, ClaimsPrincipal user,
            IAdminUserService admin, CancellationToken ct) =>
            Results.Ok(new { admin = await admin.SetAdminAsync(user.GetUserId(), userId, req.Admin, ct) }))
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite))
            .WithName("SetUserAdmin");

        group.MapGet("/users/{userId:guid}/quota", async (Guid userId, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.GetStatusAsync(userId, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));

        group.MapPut("/users/{userId:guid}/quota", async (
            Guid userId, SetQuotaRequest req, ClaimsPrincipal user, IUserQuotaService quotas,
            IAuditLog audit, CancellationToken ct) =>
        {
            // Before and after, because "who raised this person's limits" is exactly the question
            // asked afterwards.
            var before = await quotas.GetStatusAsync(userId, ct);
            var after = await quotas.SetAsync(userId, req.MaxSources, req.MaxRunsPerDay, req.MaxItemsPerRun, ct);

            await audit.RecordAsync(
                user.GetUserId(), "admin.quota.set", "user", userId,
                $"Set quota for user {userId}.", new { before, after }, asAdmin: true, ct);

            return Results.Ok(after);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite));

        // Host moderation blocklist.
        group.MapGet("/hosts", async (IAdminService admin, string? q, bool? blocked, int? limit, CancellationToken ct) =>
            Results.Ok(await admin.ListHostsAsync(q, blocked, limit, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));

        group.MapPut("/hosts", async (
            SetHostBlockedRequest req, ClaimsPrincipal user, IAdminService admin, IAuditLog audit,
            CancellationToken ct) =>
        {
            var host = await admin.SetHostBlockedAsync(req.Hostname, req.Blocked, ct);

            await audit.RecordAsync(
                user.GetUserId(),
                req.Blocked ? "admin.host.block" : "admin.host.unblock",
                "host", host.Id,
                $"{(req.Blocked ? "Blocked" : "Unblocked")} {host.Hostname}.",
                new { host.Hostname, req.Blocked, host.LinkCount },
                asAdmin: true, ct);

            return Results.Ok(host);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite));

        // Bulk re-enqueue links for enrichment. Handy after fixing an enricher bug or clearing a
        // 429 wave: pass onlyFailed=true (default) to target only links whose last fetch failed;
        // add host= to further narrow to one origin (e.g. themoviedb.org).
        group.MapPost("/links/re-enrich", async (
            IAppDbContext db,
            ILinkEnrichmentQueue queue,
            ClaimsPrincipal user,
            IAuditLog audit,
            string? host,
            bool? onlyFailed,
            CancellationToken ct) =>
        {
            var query = db.Links.AsQueryable();
            if (onlyFailed ?? true)
            {
                // Reads the status column. This used to probe the metadata bag for an
                // "enrichmentError" key, which stopped matching anything the moment failures got
                // a column of their own.
                query = query.Where(l => l.EnrichmentStatus == EnrichmentStatus.Failed
                    || l.EnrichmentStatus == EnrichmentStatus.Broken);
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

            await audit.RecordAsync(
                user.GetUserId(), "admin.links.re-enrich", "link", null,
                $"Re-queued {ids.Count} links for enrichment.",
                new { host, onlyFailed = onlyFailed ?? true, queued = ids.Count },
                asAdmin: true, ct);

            return Results.Ok(new { queued = ids.Count });
        })
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite));

        // Clear a link's adult flag globally. Detection reads a self-declared meta tag, so it
        // gets false positives; an owner can override their own playlist, but only an admin can
        // correct the link itself for everyone who has it.
        group.MapPost("/links/{id:guid}/clear-nsfw", async (
            Guid id, ClaimsPrincipal user, IAppDbContext db, IAuditLog audit, CancellationToken ct) =>
        {
            var link = await db.Links.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (link is null)
            {
                return Results.NotFound();
            }

            link.Nsfw = false;
            await db.SaveChangesAsync(ct);

            await audit.RecordAsync(
                user.GetUserId(), "admin.link.clear-nsfw", "link", link.Id,
                $"Cleared the adult flag on {link.CanonicalUrl}.", null, asAdmin: true, ct);

            return Results.Ok(new { link.Id, link.CanonicalUrl, link.Nsfw });
        })
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite));

        // The moderation queue. Moderation used to be a host blocklist and nothing else.
        group.MapGet("/reports", async (
            IContentReportService svc, ReportStatus? status, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(status, limit, cursor, ct)))
            .WithName("ListContentReports")
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));

        group.MapPost("/reports/{id:guid}/resolve", async (
            Guid id, ResolveReportRequest req, ClaimsPrincipal user, IContentReportService svc,
            CancellationToken ct) =>
            Results.Ok(await svc.ResolveAsync(user.GetUserId(), id, req.Dismiss, req.TakeDown, req.Resolution, ct)))
            .WithName("ResolveContentReport")
            .RequireAuthorization(Scopes.Policy(Scopes.AdminWrite));

        // The trail itself. Prefix-matched, so "admin." finds every admin action at once.
        group.MapGet("/audit", async (
            IAuditLog audit, string? action, Guid? actorId, Guid? targetId, int? limit, string? cursor,
            CancellationToken ct) =>
            Results.Ok(await audit.ListAsync(action, actorId, targetId, limit, cursor, ct)))
            .WithName("ListAuditLog")
            .RequireAuthorization(Scopes.Policy(Scopes.AdminRead));
    }
}
