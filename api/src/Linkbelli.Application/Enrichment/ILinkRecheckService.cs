namespace Linkbelli.Application.Enrichment;

/// <summary>
/// Re-fetches links whose metadata has gone stale or whose last attempt failed. Enrichment used
/// to run exactly once per link: a page that was rate-limited at ingest stayed blank forever, and
/// a title that changed never caught up.
/// </summary>
public interface ILinkRecheckService
{
    /// <summary>How long a successfully enriched link is trusted before it is looked at again.</summary>
    const int FreshDays = 30;

    /// <summary>Failed links back off exponentially from this, so a dead host isn't hammered.</summary>
    const int FirstRetryHours = 6;

    /// <summary>Failures stop being retried past this many attempts.</summary>
    const int MaxFailures = 6;

    /// <summary>Links re-checked per sweep, so one run can't monopolise the job server.</summary>
    const int BatchSize = 100;

    /// <summary>
    /// Queues the links that are due for another look. Returns how many were queued.
    /// </summary>
    Task<int> SweepAsync(CancellationToken ct = default);

    /// <summary>
    /// Queues one link for an immediate re-check, clearing its failure backoff first so an
    /// explicit retry isn't refused by the schedule it was already waiting on.
    /// </summary>
    Task RecheckAsync(Guid linkId, CancellationToken ct = default);
}
