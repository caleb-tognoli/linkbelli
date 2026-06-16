namespace Linkbelli.Core.Entities;

public enum SourceRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>One execution of a source. CreationTime doubles as the start time.</summary>
public class SourceRun : BaseEntity<Guid>
{
    public Guid SourceId { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public SourceRunStatus Status { get; set; } = SourceRunStatus.Running;
    public string[] ItemsFound { get; set; } = [];
    public string[] ItemsAdded { get; set; } = [];
    public string? Error { get; set; }

    public Source? Source { get; set; }
}
