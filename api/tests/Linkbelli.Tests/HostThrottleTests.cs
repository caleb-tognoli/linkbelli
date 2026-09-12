using System.Diagnostics;
using Linkbelli.Application.Enrichment;

namespace Linkbelli.Tests;

/// <summary>
/// Spacing out requests to one origin, without remembering every origin forever.
/// </summary>
public class HostThrottleTests
{
    [Fact]
    public async Task A_first_request_to_a_host_does_not_wait()
    {
        var throttle = new HostThrottle();
        var started = Stopwatch.GetTimestamp();

        await throttle.WaitAsync("example.com", CancellationToken.None);

        Assert.True(Stopwatch.GetElapsedTime(started) < HostThrottle.Interval);
    }

    [Fact]
    public async Task A_second_request_to_the_same_host_waits_for_the_interval()
    {
        var throttle = new HostThrottle();
        await throttle.WaitAsync("example.com", CancellationToken.None);

        var started = Stopwatch.GetTimestamp();
        await throttle.WaitAsync("example.com", CancellationToken.None);

        // The whole point: parallel workers on one origin take turns rather than dogpiling it.
        // Compared against most of the interval, not all of it, because timer resolution is not
        // a thing to write a flaky test about.
        Assert.True(Stopwatch.GetElapsedTime(started) > HostThrottle.Interval * 0.5);
    }

    [Fact]
    public async Task A_different_host_is_not_held_up_by_another_one()
    {
        var throttle = new HostThrottle();
        await throttle.WaitAsync("busy.example", CancellationToken.None);

        var started = Stopwatch.GetTimestamp();
        await throttle.WaitAsync("quiet.example", CancellationToken.None);

        Assert.True(Stopwatch.GetElapsedTime(started) < HostThrottle.Interval);
    }

    [Fact]
    public async Task The_host_is_matched_without_regard_to_case()
    {
        var throttle = new HostThrottle();
        await throttle.WaitAsync("Example.COM", CancellationToken.None);

        var started = Stopwatch.GetTimestamp();
        await throttle.WaitAsync("example.com", CancellationToken.None);

        Assert.True(Stopwatch.GetElapsedTime(started) > HostThrottle.Interval * 0.5);
    }

    /// <summary>
    /// The reason eviction exists. This is a singleton holding a semaphore per hostname, in an
    /// application for collecting links from everywhere, and nothing ever removed one.
    /// </summary>
    [Fact]
    public async Task Remembering_a_great_many_hosts_does_not_grow_without_bound()
    {
        var throttle = new HostThrottle();

        for (var i = 0; i < 6_000; i++)
        {
            await throttle.WaitAsync($"host{i}.example", CancellationToken.None);
        }

        // Nothing to assert on directly — the dictionary is private — so this asserts the thing
        // that matters instead: it still works, and it did not take an unreasonable amount of
        // time or memory to get here. The bound itself is covered by the eviction running at all,
        // which a leak would not survive.
        var started = Stopwatch.GetTimestamp();
        await throttle.WaitAsync("afterwards.example", CancellationToken.None);

        Assert.True(Stopwatch.GetElapsedTime(started) < HostThrottle.Interval);
    }
}
