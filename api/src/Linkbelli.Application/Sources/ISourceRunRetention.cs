namespace Linkbelli.Application.Sources;

/// <summary>
/// Trims source run history. Runs accumulate at up to ten a day per source and nobody reads one
/// a month later — except when it failed, which is the case worth keeping longer.
/// </summary>
public interface ISourceRunRetention
{
    /// <summary>Successful runs are kept this long.</summary>
    const int SucceededRetentionDays = 30;

    /// <summary>Failures are kept longer: they are the ones someone comes back to diagnose.</summary>
    const int FailedRetentionDays = 90;

    /// <summary>
    /// However old they are, this many most recent runs per source always survive — a source
    /// that runs monthly should not end up with no history at all.
    /// </summary>
    const int AlwaysKeepPerSource = 20;

    /// <summary>Removes expired runs across all sources. Returns how many rows went.</summary>
    Task<int> PruneAsync(CancellationToken ct = default);
}
