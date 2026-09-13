namespace Linkbelli.Core.Entities;

public enum SourceType
{
    Rss = 0,
    Scraper = 1,
    JsonApi = 2,

    /// <summary>
    /// Pushed to rather than polled. Every other type asks a schedule to go and look; this one
    /// waits, so links arrive when they happen instead of up to an hour later.
    /// </summary>
    Webhook = 3,
}

/// <summary>Whether a source's schedule is live.</summary>
public enum SourceStatus
{
    /// <summary>Scheduled and running on its cron.</summary>
    Active = 0,

    /// <summary>Deliberately stopped by its owner. Unscheduled; "run now" still works.</summary>
    Paused = 1,

    /// <summary>
    /// Stopped by the system after too many consecutive failures. Kept distinct from Paused so
    /// the owner can tell "I turned this off" from "this broke" — they need different actions.
    /// </summary>
    Failing = 2,
}

/// <summary>Who may attach a source to their playlists. Set at creation and immutable.</summary>
public enum SourceVisibility
{
    /// <summary>Only the owner can attach it to their own playlists.</summary>
    Private = 0,

    /// <summary>Any user can subscribe it to their own playlists.</summary>
    Shared = 1,
}

/// <summary>
/// A user-configured automatic link source ("worker"): declarative config,
/// interpreted by the matching ISourceInterpreter on a schedule.
/// Owned by a user; attachable to many playlists via PlaylistSource.
/// </summary>
public class Source : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }
    public required string Name { get; set; }
    public SourceType Type { get; set; }
    /// <summary>Governs who can subscribe it to playlists. Editable: switching Shared→Private
    /// drops other users' subscriptions (handled in SourceService.UpdateAsync).</summary>
    public SourceVisibility Visibility { get; set; } = SourceVisibility.Private;
    /// <summary>Type-specific declarative config (jsonb), validated per type.</summary>
    public required string Config { get; set; }
    /// <summary>Cron expression; enforced minimum interval applies. Kept while paused, so
    /// resuming restores the owner's original cadence rather than a default.</summary>
    public required string Schedule { get; set; }

    /// <summary>
    /// IANA time zone the <see cref="Schedule"/> is read in (e.g. "Europe/Rome"). Null means UTC,
    /// which is what every schedule silently was: "every day at 8" meant 08:00 UTC for everybody,
    /// and shifted under daylight saving for most of the world.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Whether the schedule is live. Pausing unschedules the recurring job; it does not touch
    /// <see cref="Schedule"/>, and it does not block an explicit "run now".
    /// </summary>
    public SourceStatus Status { get; set; } = SourceStatus.Active;

    /// <summary>
    /// Whether to stop pointing out that this source finds nothing.
    /// </summary>
    /// <remarks>
    /// Some sources are meant to be quiet — a feed that posts twice a year, a webhook that fires
    /// when something happens. Saying so once is help; saying so every week is noise, and noise
    /// is how somebody learns to ignore the one that is actually broken.
    /// </remarks>
    public bool MuteQuietAlerts { get; set; }
    /// <summary>Interpreter persistence between runs: ETag, Last-Modified, cursor… (jsonb).</summary>
    public string? State { get; set; }

    /// <summary>
    /// What this source is allowed to bring in — a serialised <see cref="Core.Sources.SourceFilter"/>,
    /// or null for everything, which is what every source did before filters existed.
    /// </summary>
    public string? Filter { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// Failures since the last success. A scraper whose selector broke fails on every run, and
    /// nothing was counting — so it kept failing ten times a day, indefinitely, silently.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Consecutive failures after which a source stops scheduling itself.</summary>
    public const int FailureThreshold = 5;

    public List<PlaylistSource> Playlists { get; set; } = [];
    public List<SourceRun> Runs { get; set; } = [];
}
