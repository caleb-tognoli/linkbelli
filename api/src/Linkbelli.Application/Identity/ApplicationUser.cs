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
