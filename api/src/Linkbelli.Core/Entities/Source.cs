namespace Linkbelli.Core.Entities;

public enum SourceType
{
    Rss = 0,
    Scraper = 1,
    JsonApi = 2,
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
    /// <summary>Type-specific declarative config (jsonb), validated per type.</summary>
    public required string Config { get; set; }
    /// <summary>Cron expression; enforced minimum interval applies.</summary>
    public required string Schedule { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Interpreter persistence between runs: ETag, Last-Modified, cursor… (jsonb).</summary>
    public string? State { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }

    public List<PlaylistSource> Playlists { get; set; } = [];
    public List<SourceRun> Runs { get; set; } = [];
}
