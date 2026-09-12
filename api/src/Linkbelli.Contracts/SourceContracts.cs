using Linkbelli.Core.Entities;
using Linkbelli.Core.Sources;

namespace Linkbelli.Contracts;

public record CreateSourceRequest(
    string Name,
    SourceType Type,
    IReadOnlyDictionary<string, string> Config,
    string Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility,
    /// <summary>IANA zone the schedule is read in (e.g. "Europe/Rome"). Omit for UTC.</summary>
    string? TimeZone = null,
    /// <summary>Build the config from this template instead of supplying one.</summary>
    Guid? TemplateId = null,
    /// <summary>Values for the template's fields. Ignored without a TemplateId.</summary>
    IReadOnlyDictionary<string, string>? Variables = null,
    /// <summary>What this source may bring in. Omit to accept everything it finds.</summary>
    SourceFilter? Filter = null);

public record UpdateSourceRequest(
    string? Name,
    SourceType? Type,
    IReadOnlyDictionary<string, string>? Config,
    string? Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility,
    SourceStatus? Status = null,
    /// <summary>IANA zone the schedule is read in. Omit to leave it as it is.</summary>
    string? TimeZone = null,
    /// <summary>
    /// What this source may bring in. Omit to leave it as it is; send an empty object to clear
    /// it, since null already means "don't touch".
    /// </summary>
    SourceFilter? Filter = null);

public record SourceResponse(
    Guid Id, string Name, SourceType Type, IReadOnlyDictionary<string, string> Config,
    string Schedule, SourceVisibility Visibility,
    DateTimeOffset? LastRunAt, DateTimeOffset CreationTime, Guid[] PlaylistIds,
    SourceRunStatus? LastRunStatus, SourceStatus Status = SourceStatus.Active,
    /// <summary>Failures since the last success. A source stops itself once this hits the threshold.</summary>
    int ConsecutiveFailures = 0,
    /// <summary>IANA zone the schedule is read in; null means UTC.</summary>
    string? TimeZone = null,
    /// <summary>What this source may bring in; null accepts everything.</summary>
    SourceFilter? Filter = null,
    /// <summary>
    /// For a webhook source, the token its URL is built from. Only ever returned to the owner,
    /// who has to paste it into whatever is pushing.
    /// </summary>
    string? WebhookToken = null);

/// <summary>A shared source as surfaced for subscription; no config (may contain secrets).</summary>
public record SharedSourceSummary(
    Guid Id, string Name, SourceType Type, string OwnerUsername, DateTimeOffset CreationTime);

/// <summary>A source attached to a playlist (no config). <c>OwnedByMe</c> = the caller owns the source.</summary>
public record AttachedSourceSummary(
    Guid Id, string Name, SourceType Type, string OwnerUsername, SourceVisibility Visibility, bool OwnedByMe);

/// <summary>Attach an existing source (your own, or any shared one) to a playlist you own.</summary>
public record SubscribeSourceRequest(Guid SourceId);

/// <summary>
/// One execution. <c>FoundCount</c>/<c>AddedCount</c> are the real totals; <c>ItemsFound</c> and
/// <c>ItemsAdded</c> are a capped sample of the URLs, kept for inspection rather than as a record.
/// </summary>
public record SourceRunResponse(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    SourceRunStatus Status, string[] ItemsFound, string[] ItemsAdded, string? Error,
    int FoundCount = 0, int AddedCount = 0,
    /// <summary>Discovered links the source's filter turned away.</summary>
    int SkippedCount = 0);

/// <summary>Dry-run a source config without saving, returning a few sample candidates.</summary>
public record PreviewSourceRequest(SourceType Type, IReadOnlyDictionary<string, string> Config);

public record PreviewSourceResponse(int Count, IReadOnlyList<DiscoveredLinkDto> Links);

public record DiscoveredLinkDto(string Url, string? Title);

/// <summary>A ready-made source config, with the fields it still needs from the person.</summary>
public record SourceTemplateResponse(
    Guid Id,
    string? Key,
    string Name,
    string Description,
    SourceType Type,
    string? SuggestedSchedule,
    bool Builtin,
    IReadOnlyList<SourceTemplateFieldResponse> Fields);

public record SourceTemplateFieldResponse(
    string Key, string Label, string? Placeholder, string? Help, bool Required);

/// <summary>
/// How a source has actually been doing. Every run is recorded and none of it was ever shown, so
/// a source quietly finding nothing looked exactly like one working perfectly.
/// </summary>
public record SourceHealthResponse(
    /// <summary>Runs counted, within the window below.</summary>
    int Runs,
    int WindowDays,
    int Succeeded,
    int Failed,
    /// <summary>Succeeded, as a percentage of runs. Null when it has never run.</summary>
    int? SuccessRate,
    /// <summary>Mean links discovered per successful run.</summary>
    double? AverageFound,
    /// <summary>Mean links new to the app per successful run.</summary>
    double? AverageAdded,
    /// <summary>
    /// Successful runs that discovered nothing. A scraper whose selector stopped matching
    /// succeeds every time and returns an empty list, which no status could ever reveal.
    /// </summary>
    int EmptyRuns,
    int ConsecutiveFailures,
    DateTimeOffset? LastRunAt,
    SourceRunStatus? LastRunStatus,
    string? LastError);

/// <summary>One or more links pushed to a webhook source.</summary>
public record WebhookPushRequest(IReadOnlyList<PushedLinkDto>? Links);

public record PushedLinkDto(string Url, string? Title = null);

/// <summary>What the push did, reported the same way a scheduled run is.</summary>
public record WebhookPushResponse(
    int Received, int Found, int Added, int Skipped, SourceRunStatus Status, string? Error);

/// <summary>
/// The parts of an email that matter, as forwarded by whatever received it.
/// </summary>
/// <remarks>
/// Not the raw message. Parsing MIME is a job for something that already receives mail — a
/// Cloudflare Email Worker, in the setup this ships with — and doing it here would mean this API
/// had to be an SMTP server too.
/// </remarks>
public record EmailIngestRequest(
    string From,
    string? Subject = null,
    string? Text = null,
    string? Html = null);
