using System.Net;
using System.Text;
using Linkbelli.Application.Data;
using Linkbelli.Application.Security;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Webhooks;

/// <summary>Sends one delivery, and decides what happens if it does not arrive.</summary>
public interface IWebhookDispatcher
{
    Task DeliverAsync(Guid deliveryId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WebhookDispatcher(
    IAppDbContext db,
    IHttpClientFactory http,
    ISecretProtector secrets,
    IWebhookQueue queue,
    ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    /// <summary>
    /// How long to wait before each retry: a minute, five, half an hour, two hours.
    /// </summary>
    /// <remarks>
    /// Spread over hours rather than seconds, because the usual reason a receiver is down is that
    /// it is being restarted or redeployed, and hammering it while it comes back helps nobody.
    /// </remarks>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
    ];

    /// <summary>
    /// Deliveries in flight at once, across every webhook.
    /// </summary>
    /// <remarks>
    /// A source run that feeds ten subscribed playlists is ten deliveries queued in one moment,
    /// and they share the job workers with enrichment and the source runs themselves. This keeps
    /// a slow receiver from holding every worker for ten seconds apiece. Process-wide, which is
    /// right for the single instance this app is documented to run as.
    /// </remarks>
    public const int MaxConcurrent = 4;

    private static readonly SemaphoreSlim InFlight = new(MaxConcurrent);

    public async Task DeliverAsync(Guid deliveryId, CancellationToken ct = default)
    {
        await InFlight.WaitAsync(ct);
        try
        {
            await DeliverOnceAsync(deliveryId, ct);
        }
        finally
        {
            InFlight.Release();
        }
    }

    private async Task DeliverOnceAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);

        // A job can run twice — a worker restarts mid-flight and Hangfire tries again. A delivery
        // that has already finished, either way, is left alone rather than sent a second time.
        if (delivery is null || delivery.Status is WebhookDeliveryStatus.Delivered
                or WebhookDeliveryStatus.Failed or WebhookDeliveryStatus.Cancelled)
        {
            return;
        }

        var hook = await db.Webhooks
            .AsNoTracking()
            .Where(w => w.Id == delivery.WebhookId)
            .Select(w => new { w.Id, w.Url, w.ProtectedSecret, w.Status })
            .FirstOrDefaultAsync(ct);

        // A test is sent whatever state the webhook is in: the point of pressing Test on a
        // disabled one is to see whether the receiver is fixed before turning it back on.
        if (hook is null || (hook.Status != WebhookStatus.Active && delivery.Event != WebhookEvents.Ping))
        {
            delivery.Status = WebhookDeliveryStatus.Cancelled;
            delivery.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        delivery.Attempts++;
        delivery.LastAttemptAt = now;
        delivery.NextAttemptAt = null;

        var outcome = await SendAsync(hook.Url, secrets.Unprotect(hook.ProtectedSecret), delivery, now, ct);
        delivery.ResponseStatus = outcome.Status;
        delivery.Error = outcome.Error;

        TimeSpan? retryIn = null;

        if (outcome.Delivered)
        {
            delivery.Status = WebhookDeliveryStatus.Delivered;
            delivery.DeliveredAt = now;
        }
        else if (outcome.Gone)
        {
            // 410 is a receiver saying, specifically, that this address is finished with. Going
            // on sending to it would be ignoring the one unambiguous thing it can say.
            delivery.Status = WebhookDeliveryStatus.Failed;
        }
        else if (delivery.Attempts < WebhookDelivery.MaxAttempts)
        {
            retryIn = Backoff[Math.Min(delivery.Attempts - 1, Backoff.Count - 1)];
            delivery.Status = WebhookDeliveryStatus.Retrying;
            delivery.NextAttemptAt = now + retryIn;
        }
        else
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
        }

        await db.SaveChangesAsync(ct);

        // The webhook's own counters are moved with single statements rather than through the
        // tracked row: several deliveries to one webhook can finish at the same moment, and a
        // version conflict over a failure count is not worth losing a delivery's record for.
        // A test never moves them — it is a question, not a delivery anybody is relying on.
        if (delivery.Event != WebhookEvents.Ping)
        {
            if (outcome.Delivered)
            {
                await db.Webhooks.Where(w => w.Id == hook.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.ConsecutiveFailures, 0)
                    .SetProperty(w => w.LastDeliveredAt, now)
                    .SetProperty(w => w.LastModified, now), ct);
            }
            else if (outcome.Gone)
            {
                await DisableAsync(hook.Id, "The receiver answered 410 Gone, which means it wants no more.", now, ct);
            }
            else if (delivery.Status == WebhookDeliveryStatus.Failed)
            {
                await db.Webhooks.Where(w => w.Id == hook.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.ConsecutiveFailures, w => w.ConsecutiveFailures + 1)
                    .SetProperty(w => w.LastModified, now), ct);

                await db.Webhooks
                    .Where(w => w.Id == hook.Id
                        && w.Status == WebhookStatus.Active
                        && w.ConsecutiveFailures >= Webhook.FailureThreshold)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.Status, WebhookStatus.Disabled)
                        .SetProperty(w => w.DisabledReason,
                            $"{Webhook.FailureThreshold} deliveries in a row could not be delivered. "
                            + "Turn it back on once the receiver is working.")
                        .SetProperty(w => w.LastModified, now), ct);
            }
        }

        if (retryIn is { } delay)
        {
            queue.Schedule(delivery.Id, delay);
        }
    }

    private async Task DisableAsync(Guid webhookId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        await db.Webhooks.Where(w => w.Id == webhookId).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, WebhookStatus.Disabled)
            .SetProperty(w => w.DisabledReason, reason)
            .SetProperty(w => w.LastModified, now), ct);

        logger.LogInformation("Webhook {WebhookId} disabled: {Reason}", webhookId, reason);
    }

    private async Task<Outcome> SendAsync(
        string url, string secret, WebhookDelivery delivery, DateTimeOffset now, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSignature.Header, WebhookSignature.Sign(secret, delivery.Payload, now));
        request.Headers.Add("Linkbelli-Event", delivery.Event);
        request.Headers.Add("Linkbelli-Delivery", delivery.Id.ToString());

        try
        {
            using var client = http.CreateClient(WebhookHttpClient.Name);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                return new Outcome(true, status, null, false);
            }

            if (status is >= 300 and < 400)
            {
                // Not followed, on purpose: a redirect is the receiver choosing a new address,
                // and the guard should be checking the one the owner chose.
                return new Outcome(false, status,
                    $"Answered {status} and redirects are not followed. Point the webhook at the final address.",
                    false);
            }

            var body = await ReadSomeAsync(response, ct);
            var error = $"Answered {status} {response.ReasonPhrase}".Trim()
                + (string.IsNullOrWhiteSpace(body) ? "." : $": {body}");

            return new Outcome(false, status, Clip(error), response.StatusCode == HttpStatusCode.Gone);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new Outcome(false, null,
                $"No answer within {WebhookHttpClient.Timeout.TotalSeconds:0} seconds.", false);
        }
        catch (HttpRequestException ex)
        {
            // Includes the SSRF guard refusing the address, whose message says so in plain words.
            return new Outcome(false, null, Clip(ex.Message), false);
        }
    }

    /// <summary>The start of a response body — enough to show what the receiver complained about.</summary>
    private static async Task<string> ReadSomeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new char[WebhookDelivery.MaxErrorLength];
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), ct);

            return new string(buffer, 0, read).Trim();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return string.Empty;
        }
    }

    private static string Clip(string text) =>
        text.Length <= WebhookDelivery.MaxErrorLength ? text : text[..(WebhookDelivery.MaxErrorLength - 1)] + "…";

    private sealed record Outcome(bool Delivered, int? Status, string? Error, bool Gone);
}
