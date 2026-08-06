using System.Collections.Concurrent;

namespace Linkbelli.Application.Enrichment;

/// <summary>
/// Serializes outbound enrichment requests to the same host so parallel Hangfire workers don't
/// dogpile a single origin — e.g. 3,000 TMDB links all fetching <c>themoviedb.org</c> at once
/// would trigger 429s. Callers await <see cref="WaitAsync"/> before each request; the throttle
/// ensures at least <see cref="Interval"/> elapses between two acquisitions for the same host.
/// In-memory only: single API-container deploys stay coordinated; multi-instance deploys would
/// need a distributed limiter.
/// </summary>
public interface IHostThrottle
{
    Task WaitAsync(string hostname, CancellationToken cancellationToken);
}

public sealed class HostThrottle : IHostThrottle
{
    /// <summary>Minimum time between two requests to the same host. Tuned generously for scrape
    /// targets behind edge protection (TMDB, Cloudflare-fronted sites) that 429 easily.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(750);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task WaitAsync(string hostname, CancellationToken cancellationToken)
    {
        var gate = _gates.GetOrAdd(hostname, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        // Release after Interval so the next caller can only proceed once the cooldown elapses.
        // Fire-and-forget — the release timer must run even if the caller's ct is cancelled later,
        // otherwise the gate would stay locked forever.
        _ = Task.Delay(Interval, CancellationToken.None).ContinueWith(
            _ => gate.Release(),
            TaskScheduler.Default);
    }
}
