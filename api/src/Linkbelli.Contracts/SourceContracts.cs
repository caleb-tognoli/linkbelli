using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

public record CreateSourceRequest(
    string Name,
    SourceType Type,
    IReadOnlyDictionary<string, string> Config,
    string Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility);

public record UpdateSourceRequest(
    string? Name,
    SourceType? Type,
    IReadOnlyDictionary<string, string>? Config,
    string? Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility,
    SourceStatus? Status = null);

public record SourceResponse(
    Guid Id, string Name, SourceType Type, IReadOnlyDictionary<string, string> Config,
    string Schedule, SourceVisibility Visibility,
    DateTimeOffset? LastRunAt, DateTimeOffset CreationTime, Guid[] PlaylistIds,
    SourceRunStatus? LastRunStatus, SourceStatus Status = SourceStatus.Active);

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
