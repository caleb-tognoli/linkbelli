using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Services;

/// <summary>
/// Configurable defaults applied when a new UserQuota row is first created.
/// Bind from "Quota" in appsettings. Existing rows at the old factory default are
/// automatically promoted to the configured defaults on next access.
/// </summary>
public sealed class QuotaOptions
{
    public int DefaultMaxSources { get; set; } = UserQuota.DefaultMaxSources;
    public int DefaultMaxRunsPerDay { get; set; } = UserQuota.DefaultMaxRunsPerDay;
    public int DefaultMaxItemsPerRun { get; set; } = UserQuota.DefaultMaxItemsPerRun;
}
