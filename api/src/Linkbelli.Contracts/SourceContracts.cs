using Linkbelli.Core.Entities;

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
    IReadOnlyDictionary<string, string>? Variables = null);

public record UpdateSourceRequest(
    string? Name,
    SourceType? Type,
    IReadOnlyDictionary<string, string>? Config,
    string? Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility,
    SourceStatus? Status = null,
    /// <summary>IANA zone the schedule is read in. Omit to leave it as it is.</summary>
    string? TimeZone = null);

public record SourceResponse(
    Guid Id, string Name, SourceType Type, IReadOnlyDictionary<string, string> Config,
    string Schedule, SourceVisibility Visibility,
    DateTimeOffset? LastRunAt, DateTimeOffset CreationTime, Guid[] PlaylistIds,
    SourceRunStatus? LastRunStatus, SourceStatus Status = SourceStatus.Active,
    /// <summary>Failures since the last success. A source stops itself once this hits the threshold.</summary>
    int ConsecutiveFailures = 0,
    /// <summary>IANA zone the schedule is read in; null means UTC.</summary>
    string? TimeZone = null);

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
    int FoundCount = 0, int AddedCount = 0);

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
