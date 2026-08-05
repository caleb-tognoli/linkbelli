namespace Linkbelli.Core.Entities;

public enum SourceTemplateVisibility { Private = 0, Unlisted = 1, Public = 2 }

/// <summary>
/// A pre-configured source blueprint managed by admins or users.
/// OwnerId is null for system-wide (admin-created) templates; set for user-created ones.
/// Sources created from a template store only their user-provided values in Source.Config;
/// the base config lives here and is resolved at run time by substituting {{key}} placeholders.
/// </summary>
public class SourceTemplate : BaseEntity<Guid>, ISoftDeletable
{
    /// <summary>Null = system template (admin-created, visible to all users).</summary>
    public Guid? OwnerId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public SourceType Type { get; set; }
    /// <summary>Full interpreter config with {{variable}} placeholders (jsonb).</summary>
    public required Dictionary<string, string> BaseConfig { get; set; }
    /// <summary>User-facing field definitions (jsonb). Drives the creation form.</summary>
    public required List<TemplateField> UserFields { get; set; }
    public string? DefaultSchedule { get; set; }
    public SourceTemplateVisibility Visibility { get; set; }
    public List<TemplateTag> Tags { get; set; } = [];
    public List<UserSavedTemplate> SavedBy { get; set; } = [];
}

/// <summary>Definition of a single user-editable field in a source template.</summary>
public record TemplateField(
    /// <summary>Matches the {{key}} placeholder(s) in BaseConfig values.</summary>
    string Key,
    string Label,
    string? Description,
    /// <summary>"text" | "url"</summary>
    string Type,
    bool Required,
    /// <summary>If true, the value is encrypted at rest (same mechanism as header.* keys).</summary>
    bool IsSecret);
