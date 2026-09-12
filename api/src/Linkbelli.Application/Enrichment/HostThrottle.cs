using System.Collections.Concurrent;

namespace Linkbelli.Application.Enrichment;

/// <summary>
/// Serializes outbound enrichment requests to the same host so parallel Hangfire workers don't
/// dogpile a single origin — e.g. 3,000 TMDB links all fetching <c>themoviedb.org</c> at once
/// would trigger 429s. Callers await <see cref="WaitAsync"/> before each request; the throttle
/// ensures at least <see cref="Interval"/> elapses between two acquisitions for the same host.
/// In-memory only: single API-container deploys stay coordinated; multi-instance deploys would
/// need a distributed limiter. See the README's note on running more than one instance.
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

    /// <summary>
    /// How many hosts to keep gates for.
    /// </summary>
    /// <remarks>
    /// This is a singleton holding one semaphore per distinct hostname ever enriched, and nothing
    /// ever removed one — on an application whose entire purpose is collecting links from
    /// everywhere, so the host set grows without bound. Slow, but monotonic, and a self-hosted
    /// instance is not restarted for months.
    ///
    /// The number is generous: a library spanning this many sites is unusual, and the eviction
    /// below only costs a wasted politeness delay for whichever host is dropped.
    /// </remarks>
    private const int MaxGates = 4_096;

    private readonly ConcurrentDictionary<string, Gate> _gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One host's turn-taking, plus when it was last wanted.</summary>
    private sealed class Gate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        /// <summary>Ticks, written without a lock: an approximate answer is all eviction needs.</summary>
        public long LastUsedTicks;
    }

    public async Task WaitAsync(string hostname, CancellationToken cancellationToken)
    {
        if (_gates.Count >= MaxGates)
        {
            Evict();
        }

        var gate = _gates.GetOrAdd(hostname, _ => new Gate());
        Volatile.Write(ref gate.LastUsedTicks, DateTime.UtcNow.Ticks);

        await gate.Semaphore.WaitAsync(cancellationToken);

        // Release after Interval so the next caller can only proceed once the cooldown elapses.
        // Fire-and-forget — the release timer must run even if the caller's ct is cancelled later,
        // otherwise the gate would stay locked forever.
        _ = Task.Delay(Interval, CancellationToken.None).ContinueWith(
            _ => gate.Semaphore.Release(),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Drops the least recently wanted half.
    /// </summary>
    /// <remarks>
    /// Half rather than one, so this runs rarely instead of on almost every call once the ceiling
    /// is reached. A gate that is dropped while somebody holds it is harmless: the holder still
    /// has its own reference and still releases it, and the next caller for that host simply gets
    /// a new gate and one un-spaced request.
    /// </remarks>
    private void Evict()
    {
        var oldest = _gates
            .OrderBy(pair => Volatile.Read(ref pair.Value.LastUsedTicks))
            .Take(_gates.Count / 2)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var hostname in oldest)
        {
            _gates.TryRemove(hostname, out _);
        }
    }
}
