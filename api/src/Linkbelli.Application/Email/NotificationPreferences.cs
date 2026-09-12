using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Email;

/// <summary>What somebody wants to hear about, and the one-click way to want less.</summary>
public interface INotificationPreferences
{
    Task<NotificationPreferencesResponse> GetAsync(Guid userId, CancellationToken ct = default);

    Task SetAsync(Guid userId, UpdateNotificationsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Turns off whatever the token names. Returns the kind, or null when the link was no good.
    /// </summary>
    Task<NotificationKind?> UnsubscribeAsync(string token, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class NotificationPreferences(
    IAppDbContext db, IUnsubscribeTokens tokens) : INotificationPreferences
{
    public async Task<NotificationPreferencesResponse> GetAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new NotificationPreferencesResponse(
                u.NotifyOnShare, u.NotifyOnFollow, u.NotifySourceStopped, u.NotifyWeeklyDigest))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("User not found.");

    public async Task SetAsync(
        Guid userId, UpdateNotificationsRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        // Each field optional and an omitted one left alone, like every other preference here: a
        // screen that owns one switch should not state a position on the others.
        if (request.OnShare is { } share) user.NotifyOnShare = share;
        if (request.OnFollow is { } follow) user.NotifyOnFollow = follow;
        if (request.OnSourceStopped is { } stopped) user.NotifySourceStopped = stopped;
        if (request.WeeklyDigest is { } digest) user.NotifyWeeklyDigest = digest;

        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationKind?> UnsubscribeAsync(string token, CancellationToken ct = default)
    {
        if (tokens.Read(token) is not { } parsed) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == parsed.UserId, ct);
        if (user is null) return null;

        switch (parsed.Kind)
        {
            case NotificationKind.Share: user.NotifyOnShare = false; break;
            case NotificationKind.Follow: user.NotifyOnFollow = false; break;
            case NotificationKind.SourceStopped: user.NotifySourceStopped = false; break;
            default: user.NotifyWeeklyDigest = false; break;
        }

        await db.SaveChangesAsync(ct);

        // Idempotent on purpose: a mail client that prefetches links, or somebody clicking twice,
        // must not turn this into an error page.
        return parsed.Kind;
    }
}
