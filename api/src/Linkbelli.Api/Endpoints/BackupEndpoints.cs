using System.Security.Claims;
using System.Text;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Backups;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// The snapshots taken on a user's behalf, and the way to get one back.
/// </summary>
/// <remarks>
/// Separate from <c>/export</c>, which builds a file on the spot. These already exist — the point
/// of them is that they were made before anything went wrong.
/// </remarks>
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/backups")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Backups");

        group.MapGet("/", async (ClaimsPrincipal user, IBackupService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListBackups");

        group.MapPost("/", async (ClaimsPrincipal user, IBackupService svc, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(user.GetUserId(), automatic: false, ct);

            // Nothing has changed since the last one, so there is nothing new to write. Saying so
            // is more honest than handing back a duplicate and calling it a new backup.
            return created is null
                ? Results.NoContent()
                : Results.Created($"/api/v1/backups/{created.Id}", created);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("CreateBackup");

        group.MapGet("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IBackupService svc, CancellationToken ct) =>
        {
            var (json, takenAt) = await svc.DownloadAsync(user.GetUserId(), id, ct);

            return Results.File(
                Encoding.UTF8.GetBytes(json),
                "application/json; charset=utf-8",
                $"linkbelli-backup-{takenAt:yyyy-MM-dd}.json");
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("DownloadBackup");

        group.MapDelete("/{id:guid}", async (
            Guid id, ClaimsPrincipal user, IBackupService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("DeleteBackup");
    }
}
