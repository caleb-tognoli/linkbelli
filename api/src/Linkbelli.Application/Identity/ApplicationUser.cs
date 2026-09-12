using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Application.Identity;

/// <summary>The Identity user. Guid-keyed to match OwnerId references across the domain.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Whether the user opts in to seeing NSFW content. Default false.</summary>
    public bool ShowNsfw { get; set; }

    /// <summary>
    /// Whether to ask the Internet Archive to keep a copy of the pages this user saves.
    /// </summary>
    /// <remarks>
    /// Off by default, and deliberately so: turning it on tells a third party every address the
    /// user saves. That is a choice about their own privacy, not a default worth making for them.
    /// </remarks>
    public bool ArchiveLinks { get; set; }

    /// <summary>
    /// Whether to keep periodic snapshots of this user's library.
    /// </summary>
    /// <remarks>
    /// On by default, unlike <see cref="ArchiveLinks"/>: a snapshot never leaves this server, so
    /// there is no third party to consent to. The cost is storage, and an unchanged library is
    /// not stored twice — a dormant account keeps exactly one copy.
    /// </remarks>
    public bool BackupsEnabled { get; set; } = true;

    /// <summary>
    /// When the schedule last considered this user, whether or not it made a snapshot. Kept apart
    /// from the newest snapshot's own date, which says when the library last changed.
    /// </summary>
    public DateTimeOffset? LastBackupAt { get; set; }

    /// <summary>
    /// When this user put the getting-started checklist away, or null while it still applies.
    /// </summary>
    /// <remarks>
    /// Stored on the account rather than in the browser: dismissing something once should mean
    /// once, not once per device. The checklist also hides itself when its steps are done, so
    /// this is only for people who have decided they do not want the rest of them.
    /// </remarks>
    public DateTimeOffset? OnboardingDismissedAt { get; set; }

    /// <summary>Whether to say when somebody shares a playlist with this user.</summary>
    /// <remarks>
    /// On by default, and the one hardest to argue with: a playlist shared with you is invisible
    /// until you happen to look in the right place, so without this it may as well not have been
    /// shared at all.
    /// </remarks>
    public bool NotifyOnShare { get; set; } = true;

    /// <summary>Whether to say when somebody follows one of this user's playlists.</summary>
    /// <remarks>
    /// Off by default, unlike a share. A follow is somebody else's activity rather than something
    /// done to this user's things, and it can happen repeatedly — which makes it the kind of mail
    /// people should choose rather than discover.
    /// </remarks>
    public bool NotifyOnFollow { get; set; }

    /// <summary>
    /// Whether to say when one of this user's sources gives up after repeated failures.
    /// </summary>
    /// <remarks>
    /// On by default. A source that has stopped is a playlist that has quietly stopped filling,
    /// and nothing else on the way through the app announces it.
    /// </remarks>
    public bool NotifySourceStopped { get; set; } = true;

    /// <summary>Whether to send the weekly summary.</summary>
    /// <remarks>
    /// Off by default, and this is the one worth being strict about: a recurring email nobody
    /// asked for is the single most reliable way to make somebody resent an app. The others here
    /// are rare and caused by a real event; this one arrives every week regardless.
    /// </remarks>
    public bool NotifyWeeklyDigest { get; set; }

    /// <summary>
    /// When the weekly summary was last sent, so a restart or a missed run does not send two.
    /// </summary>
    public DateTimeOffset? DigestSentAt { get; set; }

    /// <summary>
    /// When this user last looked at their feed. Null means never — everything in it is new,
    /// which is the right answer for someone who has just started following things.
    /// </summary>
    public DateTimeOffset? FeedSeenAt { get; set; }

    /// <summary>
    /// When the account was created. Identity doesn't track this, and a public profile needs it;
    /// stamped at registration.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
