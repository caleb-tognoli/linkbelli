using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Linkbelli.Application.Email;

/// <summary>
/// Schedules a notification, rather than sending it inside whoever caused it.
/// </summary>
/// <remarks>
/// Sharing a playlist should not take as long as a conversation with a mail server, and should
/// certainly not fail because one was briefly unreachable. Same shape as
/// <see cref="Enrichment.ILinkEnrichmentQueue" />, for the same reason.
/// </remarks>
public interface INotificationQueue
{
    void QueueShare(Guid recipientId, Guid playlistId, Guid actorId);

    void QueueFollow(Guid recipientId, Guid playlistId, Guid actorId);

    void QueueSourceStopped(Guid ownerId, Guid sourceId);
}

/// <summary>Sends the notifications. Called from background jobs, never from a request.</summary>
public interface INotificationService
{
    Task SendShareAsync(Guid recipientId, Guid playlistId, Guid actorId, CancellationToken ct = default);

    Task SendFollowAsync(Guid recipientId, Guid playlistId, Guid actorId, CancellationToken ct = default);

    Task SendSourceStoppedAsync(Guid ownerId, Guid sourceId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class NotificationService(
    IAppDbContext db,
    IEmailSender email,
    IUnsubscribeTokens unsubscribe,
    IOptions<EmailOptions> options,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendShareAsync(
        Guid recipientId, Guid playlistId, Guid actorId, CancellationToken ct = default)
    {
        if (await RecipientAsync(recipientId, NotificationKind.Share, ct) is not { } to) return;

        var playlist = await db.Playlists
            .Where(p => p.Id == playlistId)
            .Select(p => new { p.Name, p.Id })
            .FirstOrDefaultAsync(ct);
        if (playlist is null) return;

        var actor = await UsernameAsync(actorId, ct);
        var role = await db.PlaylistMembers
            .Where(m => m.PlaylistId == playlistId && m.UserId == recipientId)
            .Select(m => (PlaylistRole?)m.Role)
            .FirstOrDefaultAsync(ct);

        // Said plainly, because "shared with you" does not answer the question somebody actually
        // has, which is whether they can change anything.
        var what = role switch
        {
            PlaylistRole.Editor => "You can add to it, reorder it and edit what is in it.",
            PlaylistRole.Contributor => "You can add links to it.",
            _ => "You can read it.",
        };

        await SendAsync(
            to,
            recipientId,
            NotificationKind.Share,
            subject: $"{actor} shared \"{playlist.Name}\" with you",
            heading: "A playlist was shared with you",
            intro: $"{actor} shared \"{playlist.Name}\" with you. {what}",
            lines: [new EmailTemplates.NotificationLine(playlist.Name, PlaylistUrl(playlist.Id))],
            ct);
    }

    public async Task SendFollowAsync(
        Guid recipientId, Guid playlistId, Guid actorId, CancellationToken ct = default)
    {
        if (await RecipientAsync(recipientId, NotificationKind.Follow, ct) is not { } to) return;

        var playlist = await db.Playlists
            .Where(p => p.Id == playlistId)
            .Select(p => new { p.Name, p.Id })
            .FirstOrDefaultAsync(ct);
        if (playlist is null) return;

        var actor = await UsernameAsync(actorId, ct);

        await SendAsync(
            to,
            recipientId,
            NotificationKind.Follow,
            subject: $"{actor} is following \"{playlist.Name}\"",
            heading: "Somebody is following your playlist",
            intro: $"{actor} started following \"{playlist.Name}\". They will see it when you add to it.",
            lines: [new EmailTemplates.NotificationLine(playlist.Name, PlaylistUrl(playlist.Id))],
            ct);
    }

    public async Task SendSourceStoppedAsync(Guid ownerId, Guid sourceId, CancellationToken ct = default)
    {
        if (await RecipientAsync(ownerId, NotificationKind.SourceStopped, ct) is not { } to) return;

        var source = await db.Sources
            .Where(s => s.Id == sourceId && s.OwnerId == ownerId)
            .Select(s => new { s.Name, s.Id, s.ConsecutiveFailures })
            .FirstOrDefaultAsync(ct);
        if (source is null) return;

        // The last error, because "it stopped" is not actionable and "404 Not Found" is.
        var lastError = await db.SourceRuns
            .Where(r => r.SourceId == sourceId && r.Error != null)
            .OrderByDescending(r => r.CreationTime)
            .Select(r => r.Error)
            .FirstOrDefaultAsync(ct);

        var lines = new List<EmailTemplates.NotificationLine>
        {
            new(source.Name, $"{_options.PublicUrl.TrimEnd('/')}/sources"),
        };

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            lines.Add(new EmailTemplates.NotificationLine($"Last error: {lastError}"));
        }

        await SendAsync(
            to,
            ownerId,
            NotificationKind.SourceStopped,
            subject: $"\"{source.Name}\" has stopped",
            heading: "A source has stopped",
            intro:
                $"\"{source.Name}\" failed {source.ConsecutiveFailures} times in a row, so it has been " +
                "switched off rather than kept retrying. Nothing new will arrive from it until you turn it back on.",
            lines: lines,
            ct);
    }

    /// <summary>
    /// The address to write to, or null when there is nobody to tell or they asked not to be.
    /// </summary>
    private async Task<string?> RecipientAsync(Guid userId, NotificationKind kind, CancellationToken ct)
    {
        if (!email.IsConfigured) return null;

        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Email,
                Wants = kind == NotificationKind.Share ? u.NotifyOnShare
                    : kind == NotificationKind.Follow ? u.NotifyOnFollow
                    : kind == NotificationKind.SourceStopped ? u.NotifySourceStopped
                    : u.NotifyWeeklyDigest,
            })
            .FirstOrDefaultAsync(ct);

        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return null;

        return user.Wants ? user.Email : null;
    }

    /// <summary>Who did the thing. "Somebody" when the account has since gone.</summary>
    private async Task<string> UsernameAsync(Guid userId, CancellationToken ct) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync(ct)
        ?? "Somebody";

    private string PlaylistUrl(Guid playlistId) =>
        $"{_options.PublicUrl.TrimEnd('/')}/playlists/{playlistId}";

    private async Task SendAsync(
        string to,
        Guid userId,
        NotificationKind kind,
        string subject,
        string heading,
        string intro,
        IReadOnlyList<EmailTemplates.NotificationLine> lines,
        CancellationToken ct)
    {
        var message = EmailTemplates.Notification(to, subject, heading, intro, lines, _options.PublicUrl);

        // Every one of these carries its own way out, so nobody has to find a settings page to
        // stop mail they did not want.
        var token = unsubscribe.Create(userId, kind);
        var withFooter = EmailTemplates.WithUnsubscribe(
            message, $"{_options.PublicUrl.TrimEnd('/')}/unsubscribe?token={Uri.EscapeDataString(token)}", kind);

        if (!await email.SendAsync(withFooter, ct))
        {
            // Not an error: a notification is the one kind of mail whose failure nobody needs to
            // act on, and logging it as one would drown the reset failures that matter.
            logger.LogWarning("Could not send a {Kind} notification.", kind);
        }
    }
}
