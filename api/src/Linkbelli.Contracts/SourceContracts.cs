using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

public record CreateSourceRequest(
    string Name,
    SourceType Type,
    IReadOnlyDictionary<string, string> Config,
    string Schedule,
    Guid[]? PlaylistIds);

public record UpdateSourceRequest(
    string? Name,
    IReadOnlyDictionary<string, string>? Config,
    string? Schedule,
    bool? Enabled,
    Guid[]? PlaylistIds);

public record SourceResponse(
    Guid Id, string Name, SourceType Type, IReadOnlyDictionary<string, string> Config,
    string Schedule, bool Enabled, DateTimeOffset? LastRunAt, DateTimeOffset CreationTime, Guid[] PlaylistIds);

public record SourceRunResponse(
    Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
    SourceRunStatus Status, int ItemsFound, int ItemsAdded, string? Error);

/// <summary>Dry-run a source config without saving, returning a few sample candidates.</summary>
public record PreviewSourceRequest(SourceType Type, IReadOnlyDictionary<string, string> Config);

public record PreviewSourceResponse(int Count, IReadOnlyList<DiscoveredLinkDto> Links);

public record DiscoveredLinkDto(string Url, string? Title);
