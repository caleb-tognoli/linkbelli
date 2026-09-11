using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

/// <summary>An admin view of a user (for lookup → quota management).</summary>
public record AdminUserSummary(
    Guid Id, string? Username, string? Email, int PlaylistCount, int SourceCount, bool ShowNsfw);

/// <summary>An admin view of a host (moderation blocklist).</summary>
public record AdminHostSummary(Guid Id, string Hostname, bool Blocked, int LinkCount);

/// <summary>Block or unblock a host by name (created if not yet seen).</summary>
public record SetHostBlockedRequest(string Hostname, bool Blocked);

/// <summary>Queue depth and outcomes from the background job runner.</summary>
public record JobQueueStats(long Enqueued, long Processing, long Scheduled, long Failed, long Succeeded);

/// <summary>A source that is failing, and whose owner may not have noticed.</summary>
public record AdminFailingSource(
    Guid Id, string Name, string OwnerUsername, int ConsecutiveFailures, SourceStatus Status,
    DateTimeOffset? LastRunAt, string? LastError);

/// <summary>A site, how much of the collection is on it, and how much of that is unreadable.</summary>
public record AdminHostVolume(string Hostname, int LinkCount, int FailedCount);

public record AdminLinkError(
    Guid LinkId, string Url, EnrichmentStatus Status, string Error, int FailureCount,
    DateTimeOffset? LastCheckedAt);

/// <summary>
/// What is happening across the whole instance. All of it was already recorded — failing sources,
/// unreadable links, the enrichment backlog — and none of it had anywhere to be seen.
/// </summary>
public record AdminOverviewResponse(
    int Users,
    int Playlists,
    int Links,
    int Items,
    /// <summary>Links waiting to be fetched — why a filling playlist seems to creep upward.</summary>
    int PendingEnrichment,
    int BrokenLinks,
    int Sources,
    int FailingSources,
    int RunsRecently,
    int FailedRunsRecently,
    /// <summary>How many days "recently" covers.</summary>
    int RecentDays,
    /// <summary>Null when the job runner could not be reached, which is itself worth seeing.</summary>
    JobQueueStats? Jobs,
    IReadOnlyList<AdminFailingSource> TopFailingSources,
    IReadOnlyList<AdminHostVolume> TopHosts,
    IReadOnlyList<AdminLinkError> RecentErrors);

/// <summary>One recorded action: who, what, to what, and the before/after if there was one.</summary>
public record AuditEntryResponse(
    Guid Id,
    Guid? ActorId,
    /// <summary>Their name as it was at the time, kept even if the account goes.</summary>
    string ActorName,
    bool AsAdmin,
    string Action,
    string? TargetType,
    Guid? TargetId,
    string? Summary,
    /// <summary>Raw JSON, shaped per action.</summary>
    string? Details,
    DateTimeOffset At);

/// <summary>Somebody telling whoever runs this instance that something published here is wrong.</summary>
public record ContentReportResponse(
    Guid Id,
    Guid PlaylistId,
    string PlaylistName,
    string PlaylistSlug,
    string OwnerUsername,
    /// <summary>Where the playlist stands now — a taken-down one reads Private.</summary>
    PlaylistVisibility Visibility,
    string ReportedBy,
    ReportReason Reason,
    string? Note,
    ReportStatus Status,
    string? Resolution,
    DateTimeOffset ReportedAt,
    DateTimeOffset? ResolvedAt);

public record CreateReportRequest(ReportReason Reason, string? Note = null);

/// <summary>
/// Closing a report. <c>TakeDown</c> makes the playlist private, which is the one remedy short of
/// deleting somebody's work.
/// </summary>
public record ResolveReportRequest(bool Dismiss = false, bool TakeDown = false, string? Resolution = null);
