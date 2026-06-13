using Linkbelli.Application.Auth;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

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

        group.MapGet("/users/{userId:guid}/quota", async (Guid userId, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.GetStatusAsync(userId, ct)));

        group.MapPut("/users/{userId:guid}/quota", async (
            Guid userId, SetQuotaRequest req, IUserQuotaService quotas, CancellationToken ct) =>
            Results.Ok(await quotas.SetAsync(userId, req.MaxSources, req.MaxRunsPerDay, req.MaxItemsPerRun, ct)));
    }
}
