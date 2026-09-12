using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Linkbelli.Application.Email;

/// <summary>The weekly summary, for the people who asked for one.</summary>
public interface IDigestSweep
{
    /// <summary>Sends to everybody due. Returns how many went out.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one now, whatever the schedule says, and reports whether there was anything to say.
    /// </summary>
    /// <remarks>
    /// For the "send me one now" button. Somebody deciding whether to subscribe to a weekly email
    /// should be able to see one first rather than wait a week to find out.
    /// </remarks>
    Task<bool> SendOneAsync(Guid userId, bool force = false, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DigestSweep(
    IAppDbContext db,
    IEmailSender email,
    IUnsubscribeTokens unsubscribe,
    IOptions<EmailOptions> options,
    ILogger<DigestSweep> logger) : IDigestSweep
{
    /// <summary>The week the digest covers.</summary>
    public const int WindowDays = 7;

    /// <summary>
    /// How many accounts one run will send to. The job runs hourly and each account is due
    /// weekly, so this rotates rather than mailing a whole instance in one minute — which is
    /// also what a free provider's daily cap requires.
    /// </summary>
    public const int BatchSize = 50;

    /// <summary>
    /// How many of the week's links to name.
    /// </summary>
    /// <remarks>
    /// A summary that lists two hundred links is not a summary. Enough to recognise the week by,
    /// and the app is one click away for the rest.
    /// </remarks>
    public const int Highlights = 5;

    private readonly EmailOptions _options = options.Value;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        if (!email.IsConfigured) return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-WindowDays);

        var due = await db.Users
            .Where(u => u.NotifyWeeklyDigest && (u.DigestSentAt == null || u.DigestSentAt < cutoff))
            // Never-sent first, then longest-waiting. Ordered on the null-ness rather than the
            // column, because Postgres sorts NULLs last and would put the accounts that have
            // never had one behind every account that already has.
            .OrderBy(u => u.DigestSentAt != null)
            .ThenBy(u => u.DigestSentAt)
            .Take(BatchSize)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var sent = 0;

        foreach (var userId in due)
        {
            try
            {
                if (await SendOneAsync(userId, force: false, cancellationToken)) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One account's digest failing is not a reason to skip everyone after it.
                logger.LogError(ex, "Could not build a digest for {Owner}.", userId);
            }

            // Stamped whatever happened, including a week with nothing in it: otherwise a quiet
            // account is reconsidered every hour forever.
            await db.Users
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(u => u.DigestSentAt, DateTimeOffset.UtcNow), cancellationToken);
        }

        if (sent > 0)
        {
            logger.LogInformation("Sent {Sent} digests across {Considered} accounts.", sent, due.Count);
        }

        return sent;
    }

    public async Task<bool> SendOneAsync(
        Guid userId, bool force = false, CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.NotifyWeeklyDigest })
            .FirstOrDefaultAsync(cancellationToken);

        if (user?.Email is null) return false;
        if (!force && !user.NotifyWeeklyDigest) return false;

        var since = DateTimeOffset.UtcNow.AddDays(-WindowDays);
        var mine = db.PlaylistItems.Where(i => i.Playlist!.OwnerId == userId);

        var added = await mine.CountAsync(i => i.CreationTime >= since, cancellationToken);
        var unread = await mine.CountAsync(
            i => i.Status == PlaylistItemStatus.Added && i.Link!.EnrichedAt != null, cancellationToken);

        // A week in which nothing happened is not worth an email. Sending one anyway is how a
        // digest becomes the thing people unsubscribe from.
        if (!force && added == 0)
        {
            logger.LogDebug("Nothing happened for {Owner} this week; no digest.", userId);
            return false;
        }

        // Ordered before projecting: ordering over a projected record's properties is not
        // translatable, which has bitten this codebase more than once.
        //
        // Over-fetched and then deduplicated by address, because the same link can sit in several
        // playlists — and a summary that lists one page three times reads as a bug rather than as
        // a busy week.
        var candidates = await mine
            .Where(i => i.CreationTime >= since && i.Link!.EnrichedAt != null)
            .OrderByDescending(i => i.CreationTime)
            .Take(Highlights * 4)
            .Select(i => new
            {
                i.Link!.Title,
                i.Link.CanonicalUrl,
            })
            .ToListAsync(cancellationToken);

        var highlights = candidates
            .DistinctBy(c => c.CanonicalUrl)
            .Take(Highlights)
            .ToList();

        // Sources that ran and found nothing all week. Named, because knowing which one to look
        // at is the whole value of mentioning it — and a source finding nothing for a week is
        // usually a source that has broken without failing.
        var quiet = await db.Sources
            .Where(s => s.OwnerId == userId
                && s.Status == SourceStatus.Active
                && db.SourceRuns.Any(r => r.SourceId == s.Id && r.CreationTime >= since)
                && !db.SourceRuns.Any(r => r.SourceId == s.Id && r.CreationTime >= since && r.AddedCount > 0))
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .Take(Highlights)
            .ToListAsync(cancellationToken);

        var message = EmailTemplates.Digest(
            user.Email,
            _options.PublicUrl,
            added,
            unread,
            // No title means the address is all there is to call it, and printing that twice —
            // once as the name and once as the link — is worse than printing it once.
            [.. highlights.Select(h => string.IsNullOrWhiteSpace(h.Title)
                ? new EmailTemplates.NotificationLine(h.CanonicalUrl)
                : new EmailTemplates.NotificationLine(h.Title, h.CanonicalUrl))],
            quiet);

        var token = unsubscribe.Create(userId, NotificationKind.WeeklyDigest);
        var withFooter = EmailTemplates.WithUnsubscribe(
            message,
            $"{_options.PublicUrl.TrimEnd('/')}/unsubscribe?token={Uri.EscapeDataString(token)}",
            NotificationKind.WeeklyDigest);

        return await email.SendAsync(withFooter, cancellationToken);
    }
}
