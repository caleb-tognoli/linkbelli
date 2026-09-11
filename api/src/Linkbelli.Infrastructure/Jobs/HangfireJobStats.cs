using Hangfire;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Queue depth and outcomes, read from Hangfire's own monitoring API.
/// </summary>
/// <remarks>
/// Lives here rather than in the Application layer because the counts are Hangfire's, not the
/// schema's — and the admin console needs them precisely when something is wrong with the runner.
/// </remarks>
public sealed class HangfireJobStats(ILogger<HangfireJobStats> logger) : IBackgroundJobStats
{
    public JobQueueStats? Current()
    {
        try
        {
            var statistics = JobStorage.Current.GetMonitoringApi().GetStatistics();

            return new JobQueueStats(
                statistics.Enqueued,
                statistics.Processing,
                statistics.Scheduled,
                statistics.Failed,
                statistics.Succeeded);
        }
        catch (Exception ex)
        {
            // A console that 500s because the thing it is monitoring is down is useless exactly
            // when it is needed. Null says "couldn't reach the runner", which is itself a finding.
            logger.LogWarning(ex, "Could not read background job statistics.");
            return null;
        }
    }
}
