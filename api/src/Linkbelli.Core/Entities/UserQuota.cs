namespace Linkbelli.Core.Entities;

/// <summary>
/// Per-user resource limits. A row is created with defaults on first access and can be
/// overridden per user. Limits are enforced for both API-triggered and scheduled runs.
/// </summary>
public class UserQuota : BaseEntity<Guid>
{
    public const int DefaultMaxSources = 5;
    public const int DefaultMaxRunsPerDay = 10;
    public const int DefaultMaxItemsPerRun = 100;

    public Guid UserId { get; set; }
    public int MaxSources { get; set; } = DefaultMaxSources;
    public int MaxRunsPerDay { get; set; } = DefaultMaxRunsPerDay;
    public int MaxItemsPerRun { get; set; } = DefaultMaxItemsPerRun;
}
