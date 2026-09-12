using Hangfire;
using Linkbelli.Application.Email;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Hands notifications to Hangfire, so the thing that caused one does not wait for a mail server.
/// </summary>
/// <remarks>
/// Durable, which matters more here than it looks: sharing a playlist is a single click that
/// somebody will not repeat, so a notification lost to a restart is a notification nobody ever
/// finds out was missing.
/// </remarks>
public sealed class HangfireNotificationQueue(IBackgroundJobClient jobs) : INotificationQueue
{
    public void QueueShare(Guid recipientId, Guid playlistId, Guid actorId) =>
        jobs.Enqueue<INotificationService>(
            svc => svc.SendShareAsync(recipientId, playlistId, actorId, CancellationToken.None));

    public void QueueFollow(Guid recipientId, Guid playlistId, Guid actorId) =>
        jobs.Enqueue<INotificationService>(
            svc => svc.SendFollowAsync(recipientId, playlistId, actorId, CancellationToken.None));

    public void QueueSourceStopped(Guid ownerId, Guid sourceId) =>
        jobs.Enqueue<INotificationService>(
            svc => svc.SendSourceStoppedAsync(ownerId, sourceId, CancellationToken.None));
}
