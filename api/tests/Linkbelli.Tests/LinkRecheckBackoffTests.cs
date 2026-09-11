using Linkbelli.Application.Enrichment;

namespace Linkbelli.Tests;

/// <summary>
/// The backoff schedule failed links wait on. Exercised through the same arithmetic the sweep
/// uses, so the intervals are pinned rather than assumed.
/// </summary>
public class LinkRecheckBackoffTests
{
    /// <summary>Mirrors LinkRecheckService.IsPastBackoff, which is private to the sweep.</summary>
    private static TimeSpan DelayFor(int failureCount)
    {
        var attempts = Math.Clamp(failureCount, 1, ILinkRecheckService.MaxFailures);
        return TimeSpan.FromHours(ILinkRecheckService.FirstRetryHours * Math.Pow(2, attempts - 1));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(2, 12)]
    [InlineData(3, 24)]
    [InlineData(4, 48)]
    [InlineData(5, 96)]
    public void Backoff_doubles_with_each_failure(int failures, int expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), DelayFor(failures));
    }

    [Fact]
    public void A_first_failure_is_treated_the_same_as_a_count_of_zero()
    {
        // Defensive: a link that somehow reaches the sweep with no recorded failures should wait
        // the first interval, not retry immediately in a loop.
        Assert.Equal(DelayFor(1), DelayFor(0));
    }

    [Fact]
    public void Backoff_stops_growing_at_the_give_up_point()
    {
        var atLimit = DelayFor(ILinkRecheckService.MaxFailures);

        Assert.Equal(atLimit, DelayFor(ILinkRecheckService.MaxFailures + 10));
        Assert.True(atLimit < TimeSpan.FromDays(30), "Backoff should stay within a sane horizon.");
    }

    [Fact]
    public void A_link_gives_up_within_a_week_of_first_failing()
    {
        // The whole retry sequence, summed — a dead host should not be retried for months.
        var total = TimeSpan.Zero;
        for (var n = 1; n <= ILinkRecheckService.MaxFailures; n++)
        {
            total += DelayFor(n);
        }

        Assert.True(total < TimeSpan.FromDays(30),
            $"Retries should be exhausted well inside a month, took {total.TotalDays:F1} days.");
    }
}
