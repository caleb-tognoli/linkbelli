namespace Linkbelli.Core.Entities;

public enum SourceType
{
    Rss = 0,
    Scraper = 1,
    JsonApi = 2,
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
    /// <summary>
    /// Type-specific declarative config (jsonb).
    /// For manual sources: the full interpreter config.
    /// For template sources: user-provided substitution values only ({{key}} → value).
    /// </summary>
    public string? Config { get; set; }
    /// <summary>If set, this source was created from a template. Config holds user params only.</summary>
    public Guid? TemplateId { get; set; }
    public SourceTemplate? Template { get; set; }
    /// <summary>Cron expression; enforced minimum interval applies.</summary>
    public required string Schedule { get; set; }
    /// <summary>Interpreter persistence between runs: ETag, Last-Modified, cursor… (jsonb).</summary>
    public string? State { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }

    public List<PlaylistSource> Playlists { get; set; } = [];
    public List<SourceRun> Runs { get; set; } = [];
}
