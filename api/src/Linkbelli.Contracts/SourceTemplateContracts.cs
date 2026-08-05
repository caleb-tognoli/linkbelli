using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

public record TemplateFieldDto(
    string Key,
    string Label,
    string? Description,
    string Type,
    bool Required,
    bool IsSecret);

public record SourceTemplateResponse(
    Guid Id,
    Guid? OwnerId,
    string Name,
    string? Description,
    SourceType Type,
    IReadOnlyDictionary<string, string> BaseConfig,
    IReadOnlyList<TemplateFieldDto> UserFields,
    string? DefaultSchedule,
    SourceTemplateVisibility Visibility,
    IReadOnlyList<string> Tags,
    bool OwnedByMe,
    bool SavedByMe,
    DateTimeOffset CreationTime);

/// <summary>Summary returned in template gallery (no BaseConfig exposed to regular users).</summary>
public record SourceTemplateSummary(
    Guid Id,
    Guid? OwnerId,
    string Name,
    string? Description,
    SourceType Type,
    IReadOnlyList<TemplateFieldDto> UserFields,
    string? DefaultSchedule,
    SourceTemplateVisibility Visibility,
    IReadOnlyList<string> Tags,
    bool OwnedByMe,
    bool SavedByMe,
    DateTimeOffset CreationTime);

public record CreateSourceTemplateRequest(
    string Name,
    string? Description,
    SourceType Type,
    IReadOnlyDictionary<string, string> BaseConfig,
    IReadOnlyList<TemplateFieldDto> UserFields,
    string? DefaultSchedule,
    SourceTemplateVisibility Visibility,
    IReadOnlyList<string>? Tags,
    /// <summary>Admin only: when true, creates a system template (OwnerId = null).</summary>
    bool IsSystem = false);

public record UpdateSourceTemplateRequest(
    string? Name,
    string? Description,
    IReadOnlyDictionary<string, string>? BaseConfig,
    IReadOnlyList<TemplateFieldDto>? UserFields,
    string? DefaultSchedule,
    SourceTemplateVisibility? Visibility,
    IReadOnlyList<string>? Tags);

/// <summary>Create a source from a template — user supplies only the template's UserFields.</summary>
public record CreateTemplateSourceRequest(
    Guid TemplateId,
    string Name,
    IReadOnlyDictionary<string, string> UserParams,
    string Schedule,
    Guid[]? PlaylistIds,
    SourceVisibility? Visibility);
