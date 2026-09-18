using System.Net;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Http;
using Linkbelli.Application.Security;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Linkbelli.Application.Webhooks;

/// <summary>Making, changing and inspecting somebody's webhooks.</summary>
public interface IWebhookService
{
    IReadOnlyList<WebhookEventInfo> Events();

    Task<IReadOnlyList<WebhookResponse>> ListAsync(Guid ownerId, CancellationToken ct = default);

    Task<WebhookWithSecretResponse> CreateAsync(Guid ownerId, CreateWebhookRequest request, CancellationToken ct = default);

    Task<WebhookResponse> UpdateAsync(Guid ownerId, Guid id, UpdateWebhookRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    Task<WebhookWithSecretResponse> RotateSecretAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>Sends a <c>ping</c> and returns the delivery, so the caller can watch it land.</summary>
    Task<WebhookDeliveryResponse> TestAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<WebhookDeliveryResponse>> ListDeliveriesAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>Sends an earlier delivery's exact body again, as a new delivery.</summary>
    Task<WebhookDeliveryResponse> RedeliverAsync(Guid ownerId, Guid deliveryId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WebhookService(
    IAppDbContext db,
    ISecretProtector secrets,
    IWebhookEvents events,
    IWebhookQueue queue,
    IOptions<WebhookOptions> options,
    ILogger<WebhookService> logger) : IWebhookService
{
    /// <summary>How many recent deliveries a webhook shows.</summary>
    public const int DeliveriesShown = 50;

    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [WebhookEvents.ItemsAdded] = "Links saved into a playlist — by hand, by a source, an import or a paste.",
        [WebhookEvents.ItemsFinished] = "Links marked finished, including by reading to the end.",
        [WebhookEvents.ItemsTagged] = "A tag put on links, by hand or by a rule.",
        [WebhookEvents.SourceStopped] = "A source that kept failing and has been stopped.",
        [WebhookEvents.HighlightCreated] = "A passage marked in an article.",
    };

    public IReadOnlyList<WebhookEventInfo> Events() =>
        [.. WebhookEvents.Subscribable.Select(e => new WebhookEventInfo(e, Descriptions[e]))];

    public async Task<IReadOnlyList<WebhookResponse>> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        var hooks = await db.Webhooks
            .AsNoTracking()
            .Where(w => w.OwnerId == ownerId)
            .OrderBy(w => w.CreationTime)
            .ToListAsync(ct);

        return [.. hooks.Select(ToResponse)];
    }

    public async Task<WebhookWithSecretResponse> CreateAsync(
        Guid ownerId, CreateWebhookRequest request, CancellationToken ct = default)
    {
        if (await db.Webhooks.CountAsync(w => w.OwnerId == ownerId, ct) >= Webhook.MaxPerOwner)
        {
            throw new ValidationException(
                "webhooks", $"There is a limit of {Webhook.MaxPerOwner} webhooks on one account.");
        }

        var secret = WebhookSignature.NewSecret();
        var hook = new Webhook
        {
            OwnerId = ownerId,
            Url = ValidUrl(request.Url),
            Events = ValidEvents(request.Events),
            Description = ValidDescription(request.Description),
            ProtectedSecret = secrets.Protect(secret),
        };

        db.Webhooks.Add(hook);
        await db.SaveChangesAsync(ct);

        return new WebhookWithSecretResponse(ToResponse(hook), secret);
    }

    public async Task<WebhookResponse> UpdateAsync(
        Guid ownerId, Guid id, UpdateWebhookRequest request, CancellationToken ct = default)
    {
        var hook = await FindAsync(ownerId, id, ct);

        if (request.Url is not null)
        {
            hook.Url = ValidUrl(request.Url);
        }

        if (request.Events is not null)
        {
            hook.Events = ValidEvents(request.Events);
        }

        if (request.Description is not null)
        {
            hook.Description = ValidDescription(request.Description);
        }

        if (request.Active is { } active)
        {
            if (active && hook.Status != WebhookStatus.Active)
            {
                // Turning it on is the owner saying the receiver is fixed, so the count that
                // turned it off starts again rather than tripping on the next failure.
                hook.Status = WebhookStatus.Active;
                hook.ConsecutiveFailures = 0;
                hook.DisabledReason = null;
            }
            else if (!active && hook.Status == WebhookStatus.Active)
            {
                hook.Status = WebhookStatus.Paused;
            }
        }

        await db.SaveChangesAsync(ct);
        return ToResponse(hook);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var hook = await FindAsync(ownerId, id, ct);

        // Anything still queued for it finds no webhook when it runs and is marked cancelled —
        // so nothing leaves for an address its owner has just said to stop using.
        hook.DeletionTime = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WebhookWithSecretResponse> RotateSecretAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var hook = await FindAsync(ownerId, id, ct);

        // Immediate, with no overlap window. Deliveries already queued are signed when they are
        // sent, so they carry the new secret too — a receiver needs updating before this is
        // pressed, not after, which the screen says.
        var secret = WebhookSignature.NewSecret();
        hook.ProtectedSecret = secrets.Protect(secret);
        await db.SaveChangesAsync(ct);

        return new WebhookWithSecretResponse(ToResponse(hook), secret);
    }

    public async Task<WebhookDeliveryResponse> TestAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var hook = await FindAsync(ownerId, id, ct);

        var deliveryId = await events.PingAsync(hook.Id, ct)
            ?? throw new InvalidOperationException("The test delivery could not be queued.");

        return await DeliveryAsync(deliveryId, ct);
    }

    public async Task<IReadOnlyList<WebhookDeliveryResponse>> ListDeliveriesAsync(
        Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var hook = await FindAsync(ownerId, id, ct);

        return await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.WebhookId == hook.Id)
            .OrderByDescending(d => d.CreationTime).ThenByDescending(d => d.Id)
            .Take(DeliveriesShown)
            .Select(ToDeliveryResponse)
            .ToListAsync(ct);
    }

    public async Task<WebhookDeliveryResponse> RedeliverAsync(Guid ownerId, Guid deliveryId, CancellationToken ct = default)
    {
        var original = await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.Id == deliveryId && d.Webhook!.OwnerId == ownerId)
            .Select(d => new { d.WebhookId, d.Event, d.Payload })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Delivery not found.");

        // A new row with the old body, so the history of what happened the first time is kept.
        // The id inside the body stays the original's: to a receiver it is the same event, and
        // one that already handled it is right to drop it.
        var copy = new WebhookDelivery
        {
            Id = Guid.CreateVersion7(),
            WebhookId = original.WebhookId,
            Event = original.Event,
            Payload = original.Payload,
        };

        db.WebhookDeliveries.Add(copy);
        await db.SaveChangesAsync(ct);
        queue.Enqueue(copy.Id);

        logger.LogInformation("Redelivering {DeliveryId} as {CopyId}.", deliveryId, copy.Id);
        return await DeliveryAsync(copy.Id, ct);
    }

    private async Task<WebhookDeliveryResponse> DeliveryAsync(Guid id, CancellationToken ct) =>
        await db.WebhookDeliveries
            .AsNoTracking()
            .Where(d => d.Id == id)
            .Select(ToDeliveryResponse)
            .FirstAsync(ct);

    private async Task<Webhook> FindAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        await db.Webhooks.FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Webhook not found.");

    private string ValidUrl(string? raw)
    {
        var url = raw?.Trim();
        if (string.IsNullOrEmpty(url)
            || url.Length > Webhook.MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new ValidationException("url", "A webhook needs a full http:// or https:// address.");
        }

        // Only what can be told from the address alone. A name that resolves somewhere it should
        // not is caught when the connection is made, on every delivery; this is just the obvious
        // case turned away with a reason, instead of accepted and then failing forever.
        var refused = uri.IsLoopback
            || (IPAddress.TryParse(uri.IdnHost, out var address)
                && !SsrfProtection.IsPublic(address)
                && !(options.Value.AllowPrivateNetworks && SsrfProtection.IsPrivateNetwork(address)));

        if (refused)
        {
            throw new ValidationException(
                "url",
                options.Value.AllowPrivateNetworks
                    ? "That address is on a network this server will not send to."
                    : "That address is on a private network. Sending to one has to be allowed by "
                        + "whoever runs this server.");
        }

        return url;
    }

    private static string[] ValidEvents(IReadOnlyList<string>? requested)
    {
        var chosen = (requested ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct().ToArray();

        if (chosen.Length == 0)
        {
            throw new ValidationException("events", "Choose at least one event to be sent.");
        }

        var unknown = chosen.FirstOrDefault(e => !WebhookEvents.IsSubscribable(e));
        if (unknown is not null)
        {
            throw new ValidationException(
                "events",
                $"'{unknown}' is not an event. The events are: {string.Join(", ", WebhookEvents.Subscribable)}.");
        }

        return chosen;
    }

    private static string? ValidDescription(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text.Length > Webhook.MaxDescriptionLength)
        {
            throw new ValidationException(
                "description", $"A description can be at most {Webhook.MaxDescriptionLength} characters.");
        }

        return text;
    }

    private static WebhookResponse ToResponse(Webhook w) =>
        new(w.Id, w.Url, w.Description, w.Events, w.Status.ToString(), w.ConsecutiveFailures,
            w.DisabledReason, w.LastDeliveredAt, w.CreationTime);

    private static readonly System.Linq.Expressions.Expression<Func<WebhookDelivery, WebhookDeliveryResponse>>
        ToDeliveryResponse = d => new WebhookDeliveryResponse(
            d.Id, d.Event, d.Status.ToString(), d.Attempts, d.ResponseStatus, d.Error,
            d.CreationTime, d.LastAttemptAt, d.NextAttemptAt, d.DeliveredAt);
}

/// <summary>Forgets finished deliveries after a while.</summary>
public interface IWebhookDeliveryRetention
{
    Task<int> PurgeAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class WebhookDeliveryRetention(IAppDbContext db, ILogger<WebhookDeliveryRetention> logger)
    : IWebhookDeliveryRetention
{
    /// <summary>
    /// Long enough to answer "did that fire last week", short enough that a busy webhook's log
    /// does not become the largest table on the server.
    /// </summary>
    public const int RetentionDays = 30;

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);

        // Only finished ones: something still retrying is a delivery in progress, not history.
        var removed = await db.WebhookDeliveries
            .IgnoreQueryFilters()
            .Where(d => d.CreationTime < cutoff
                && (d.Status == WebhookDeliveryStatus.Delivered
                    || d.Status == WebhookDeliveryStatus.Failed
                    || d.Status == WebhookDeliveryStatus.Cancelled))
            .ExecuteDeleteAsync(ct);

        if (removed > 0)
        {
            logger.LogInformation("Purged {Count} old webhook deliveries.", removed);
        }

        return removed;
    }
}
