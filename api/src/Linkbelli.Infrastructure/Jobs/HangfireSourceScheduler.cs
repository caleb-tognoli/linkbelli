using Hangfire;
using Linkbelli.Application.Sources;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>Maps a source to a Hangfire recurring job (id "source:{guid}") and ad-hoc runs.</summary>
public sealed class HangfireSourceScheduler(
    IRecurringJobManager recurringJobs,
    IBackgroundJobClient jobs) : ISourceScheduler
{
    public static string RecurringJobId(Guid sourceId) => $"source:{sourceId}";

    public void Schedule(Guid sourceId, string cron) =>
        recurringJobs.AddOrUpdate<ISourceRunner>(
            RecurringJobId(sourceId), runner => runner.RunAsync(sourceId, CancellationToken.None), cron);

    public void Unschedule(Guid sourceId) =>
        recurringJobs.RemoveIfExists(RecurringJobId(sourceId));

    public void TriggerNow(Guid sourceId) =>
        jobs.Enqueue<ISourceRunner>(runner => runner.RunAsync(sourceId, CancellationToken.None));
}
