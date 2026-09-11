using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Services;

/// <summary>
/// Records what was done, by whom, to what. Admin actions reach into other people's data and
/// several user actions destroy something outright; neither left any trace.
/// </summary>
public interface IAuditLog
{
    /// <summary>
    /// Writes an entry. Never throws: failing to record an action must not undo the action, which
    /// has usually already happened by the time this is called.
    /// </summary>
    Task RecordAsync(
        Guid? actorId,
        string action,
        string? targetType = null,
        Guid? targetId = null,
        string? summary = null,
        object? details = null,
        bool asAdmin = false,
        CancellationToken ct = default);

    /// <summary>The trail, newest first. Admin-only reading.</summary>
    Task<PagedResult<AuditEntryResponse>> ListAsync(
        string? action, Guid? actorId, Guid? targetId, int? limit, string? cursor,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AuditLog(IAppDbContext db, ILogger<AuditLog> logger) : IAuditLog
{
    private const int MaxLimit = 200;

    public async Task RecordAsync(
        Guid? actorId,
        string action,
        string? targetType = null,
        Guid? targetId = null,
        string? summary = null,
        object? details = null,
        bool asAdmin = false,
        CancellationToken ct = default)
    {
        try
        {
            var name = actorId is null
                ? "system"
                : await db.Users.Where(u => u.Id == actorId).Select(u => u.UserName!).FirstOrDefaultAsync(ct)
                  ?? "(deleted user)";

            db.AuditEntries.Add(new AuditEntry
            {
                ActorId = actorId,
                ActorName = name,
                AsAdmin = asAdmin,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Summary = summary,
                Details = details is null ? null : JsonSerializer.Serialize(details),
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The action being audited has already happened. Undoing it because the note about it
            // failed would be the worse outcome by a wide margin.
            logger.LogError(ex, "Failed to record audit entry {Action}.", action);
        }
    }

    public async Task<PagedResult<AuditEntryResponse>> ListAsync(
        string? action, Guid? actorId, Guid? targetId, int? limit, string? cursor,
        CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var offset = Cursor.TryDecode(cursor, out var payload) && int.TryParse(payload, out var parsed)
            ? Math.Max(0, parsed)
            : 0;

        var query = db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            // Prefix, so "admin." finds every admin action without listing them all.
            var prefix = action.Trim().ToLowerInvariant();
            query = query.Where(e => e.Action.StartsWith(prefix));
        }

        if (actorId is { } actor) query = query.Where(e => e.ActorId == actor);
        if (targetId is { } target) query = query.Where(e => e.TargetId == target);

        var rows = await query
            .OrderByDescending(e => e.CreationTime)
            .ThenByDescending(e => e.Id)
            .Skip(offset)
            .Take(take + 1)
            .Select(e => new AuditEntryResponse(
                e.Id, e.ActorId, e.ActorName, e.AsAdmin, e.Action, e.TargetType, e.TargetId,
                e.Summary, e.Details, e.CreationTime))
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.Encode((offset + take).ToString());
        }

        return new PagedResult<AuditEntryResponse>(rows, next);
    }
}
