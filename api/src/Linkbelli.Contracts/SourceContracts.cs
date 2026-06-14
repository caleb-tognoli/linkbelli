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
    IReadOnlyDictionary<string, string>? Config,
    string? Schedule,
    bool? Enabled,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility);

public record SourceResponse(
    Guid Id, string Name, SourceType Type, IReadOnlyDictionary<string, string> Config,
    string Schedule, bool Enabled, SourceVisibility Visibility,
    DateTimeOffset? LastRunAt, DateTimeOffset CreationTime, Guid[] PlaylistIds);

/// <summary>A shared source as surfaced for subscription; no config (may contain secrets).</summary>
public record SharedSourceSummary(
    Guid Id, string Name, SourceType Type, string OwnerUsername, DateTimeOffset CreationTime);

/// <summary>A source attached to a playlist (no config). <c>OwnedByMe</c> = the caller owns the source.</summary>
public record AttachedSourceSummary(
    Guid Id, string Name, SourceType Type, string OwnerUsername, SourceVisibility Visibility, bool OwnedByMe);

/// <summary>Attach an existing source (your own, or any shared one) to a playlist you own.</summary>
public record SubscribeSourceRequest(Guid SourceId);

public record SourceRunResponse(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    SourceRunStatus Status, int ItemsFound, int ItemsAdded, string? Error);

/// <summary>Dry-run a source config without saving, returning a few sample candidates.</summary>
public record PreviewSourceRequest(SourceType Type, IReadOnlyDictionary<string, string> Config);

public record PreviewSourceResponse(int Count, IReadOnlyList<DiscoveredLinkDto> Links);

public record DiscoveredLinkDto(string Url, string? Title);
