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
    /// <summary>
    /// How many URLs a run keeps for inspection. Runs are the fastest-growing table in the
    /// schema — a full list of up to a hundred canonical URLs per run, ten runs a day per
    /// source, duplicated every one of them from Link.CanonicalUrl.
    /// </summary>
    public const int SampleSize = 20;

    public Guid SourceId { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public SourceRunStatus Status { get; set; } = SourceRunStatus.Running;

    /// <summary>Total URLs the run discovered.</summary>
    public int FoundCount { get; set; }

    /// <summary>Of those, how many were new to the application.</summary>
    public int AddedCount { get; set; }

    /// <summary>Up to <see cref="SampleSize"/> of the discovered URLs, for inspection.</summary>
    public string[] ItemsFound { get; set; } = [];

    /// <summary>Up to <see cref="SampleSize"/> of the newly added URLs, for inspection.</summary>
    public string[] ItemsAdded { get; set; } = [];

    public string? Error { get; set; }

    public Source? Source { get; set; }
}
