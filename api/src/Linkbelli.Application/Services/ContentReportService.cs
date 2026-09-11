using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Reporting something published here, and the queue it lands in. Moderation was a host blocklist
/// and nothing else: a visitor who found something had no way to say so.
/// </summary>
public interface IContentReportService
{
    Task<ContentReportResponse> ReportAsync(
        Guid reporterId, string username, string slug, ReportReason reason, string? note,
        CancellationToken ct = default);

    /// <summary>The queue, open first. Admin-only.</summary>
    Task<PagedResult<ContentReportResponse>> ListAsync(
        ReportStatus? status, int? limit, string? cursor, CancellationToken ct = default);

    /// <summary>
    /// Closes a report. <paramref name="takeDown"/> makes the playlist private, which is the one
    /// remedy short of deleting somebody's work.
    /// </summary>
    Task<ContentReportResponse> ResolveAsync(
        Guid adminId, Guid reportId, bool dismiss, bool takeDown, string? resolution,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ContentReportService(IAppDbContext db, IAuditLog audit) : IContentReportService
{
    private const int MaxLimit = 100;

    /// <summary>Longest note kept. Enough to explain; not enough to be an essay.</summary>
    public const int MaxNoteLength = 1000;

    public async Task<ContentReportResponse> ReportAsync(
        Guid reporterId, string username, string slug, ReportReason reason, string? note,
        CancellationToken ct = default)
    {
        var normalized = username.Trim().ToUpperInvariant();

        var playlist = await db.Playlists
            .Where(p => p.Slug == slug
                && p.Visibility != PlaylistVisibility.Private
                && db.Users.Any(u => u.Id == p.OwnerId && u.NormalizedUserName == normalized))
            .Select(p => new { p.Id, p.OwnerId, p.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Playlist not found.");

        if (playlist.OwnerId == reporterId)
        {
            throw new ValidationException("playlist", "You can change your own playlist directly.");
        }

        // One open report per person per playlist: a second one adds nothing to the queue except
        // another row to read.
        var existing = await db.ContentReports.FirstOrDefaultAsync(
            r => r.PlaylistId == playlist.Id && r.ReporterId == reporterId && r.Status == ReportStatus.Open,
            ct);

        if (existing is not null)
        {
            return await ToResponseAsync(existing, ct);
        }

        var report = new ContentReport
        {
            ReporterId = reporterId,
            PlaylistId = playlist.Id,
            Reason = reason,
            Note = Trim(note),
        };

        db.ContentReports.Add(report);
        await db.SaveChangesAsync(ct);

        return await ToResponseAsync(report, ct);
    }

    public async Task<PagedResult<ContentReportResponse>> ListAsync(
        ReportStatus? status, int? limit, string? cursor, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, MaxLimit);
        var offset = Cursor.TryDecode(cursor, out var payload) && int.TryParse(payload, out var parsed)
            ? Math.Max(0, parsed)
            : 0;

        var query = db.ContentReports.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        var rows = await query
            // Open first, then newest: a queue sorted purely by date buries what still needs doing.
            .OrderBy(r => r.Status == ReportStatus.Open ? 0 : 1)
            .ThenByDescending(r => r.CreationTime)
            .Skip(offset)
            .Take(take + 1)
            .Select(r => new ContentReportResponse(
                r.Id,
                r.PlaylistId,
                r.Playlist!.Name,
                r.Playlist.Slug,
                db.Users.Where(u => u.Id == r.Playlist.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
                r.Playlist.Visibility,
                db.Users.Where(u => u.Id == r.ReporterId).Select(u => u.UserName!).FirstOrDefault()!,
                r.Reason,
                r.Note,
                r.Status,
                r.Resolution,
                r.CreationTime,
                r.ResolvedAt))
            .ToListAsync(ct);

        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.Encode((offset + take).ToString());
        }

        return new PagedResult<ContentReportResponse>(rows, next);
    }

    public async Task<ContentReportResponse> ResolveAsync(
        Guid adminId, Guid reportId, bool dismiss, bool takeDown, string? resolution,
        CancellationToken ct = default)
    {
        var report = await db.ContentReports.FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("Report not found.");

        if (takeDown)
        {
            var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == report.PlaylistId, ct);
            if (playlist is not null && playlist.Visibility != PlaylistVisibility.Private)
            {
                var before = playlist.Visibility;

                // Made private rather than deleted. It stops being published, and its owner keeps
                // their work — deleting somebody's collection over a report is not recoverable.
                playlist.Visibility = PlaylistVisibility.Private;

                await audit.RecordAsync(
                    adminId, "admin.playlist.takedown", "playlist", playlist.Id,
                    $"Took down \"{playlist.Name}\" after a report.",
                    new { before, after = PlaylistVisibility.Private, reportId },
                    asAdmin: true, ct);
            }
        }

        report.Status = dismiss ? ReportStatus.Dismissed : ReportStatus.Resolved;
        report.ResolvedBy = adminId;
        report.ResolvedAt = DateTimeOffset.UtcNow;
        report.Resolution = Trim(resolution);

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            adminId,
            dismiss ? "admin.report.dismiss" : "admin.report.resolve",
            "report", report.Id,
            $"{(dismiss ? "Dismissed" : "Resolved")} a {report.Reason} report.",
            new { report.Reason, takeDown, resolution = report.Resolution },
            asAdmin: true, ct);

        return await ToResponseAsync(report, ct);
    }

    private async Task<ContentReportResponse> ToResponseAsync(ContentReport report, CancellationToken ct)
    {
        var playlist = await db.Playlists
            .Where(p => p.Id == report.PlaylistId)
            .Select(p => new
            {
                p.Name,
                p.Slug,
                p.Visibility,
                Owner = db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName!).FirstOrDefault()!,
            })
            .FirstAsync(ct);

        var reporter = await db.Users
            .Where(u => u.Id == report.ReporterId)
            .Select(u => u.UserName!)
            .FirstOrDefaultAsync(ct) ?? "(deleted user)";

        return new ContentReportResponse(
            report.Id, report.PlaylistId, playlist.Name, playlist.Slug, playlist.Owner,
            playlist.Visibility, reporter, report.Reason, report.Note, report.Status,
            report.Resolution, report.CreationTime, report.ResolvedAt);
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNoteLength ? trimmed[..MaxNoteLength] : trimmed;
    }
}
