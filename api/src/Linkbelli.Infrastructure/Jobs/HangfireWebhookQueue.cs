using Hangfire;
using Linkbelli.Application.Webhooks;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Deliveries as Hangfire jobs, so a restart does not lose one that was waiting to go and a
/// retry scheduled for two hours from now still happens.
/// </summary>
/// <remarks>
/// Retrying is the dispatcher's decision, not Hangfire's: it needs to know when a delivery has
/// run out of attempts, to count that against the webhook, and Hangfire's own retries would hide
/// that. So the job itself is never retried by Hangfire.
/// </remarks>
public sealed class HangfireWebhookQueue(IBackgroundJobClient jobs) : IWebhookQueue
{
    public void Enqueue(Guid deliveryId) =>
        jobs.Enqueue<WebhookDeliveryJob>(job => job.RunAsync(deliveryId, CancellationToken.None));

    public void Schedule(Guid deliveryId, TimeSpan delay) =>
        jobs.Schedule<WebhookDeliveryJob>(job => job.RunAsync(deliveryId, CancellationToken.None), delay);
}

/// <summary>The job itself: a thin wrapper that tells Hangfire not to retry it.</summary>
public sealed class WebhookDeliveryJob(IWebhookDispatcher dispatcher)
{
    [AutomaticRetry(Attempts = 0)]
    public Task RunAsync(Guid deliveryId, CancellationToken ct) => dispatcher.DeliverAsync(deliveryId, ct);
}
