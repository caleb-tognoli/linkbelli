namespace Linkbelli.Core.Entities;

public enum WebhookDeliveryStatus
{
    Pending = 0,

    /// <summary>Failed at least once and scheduled to try again.</summary>
    Retrying = 1,

    Delivered = 2,

    /// <summary>Ran out of attempts.</summary>
    Failed = 3,

    /// <summary>Not sent, because the webhook was paused, disabled or deleted before it went.</summary>
    Cancelled = 4,
}

/// <summary>
/// One event on its way to one webhook, and what happened when it was sent.
/// </summary>
/// <remarks>
/// A log as much as a queue: "did it fire, and what did my endpoint say" is the first question
/// anybody asks about a webhook, and without this the only answer is to go and read the
/// receiver's logs — which, for a Home Assistant box on a shelf, somebody may not have.
/// </remarks>
public class WebhookDelivery : BaseEntity<Guid>
{
    /// <summary>Attempts before a delivery gives up.</summary>
    public const int MaxAttempts = 5;

    /// <summary>How much of an error or a response body is kept to show back.</summary>
    public const int MaxErrorLength = 500;

    public Guid WebhookId { get; set; }

    public string Event { get; set; } = string.Empty;

    /// <summary>
    /// The exact body that is sent, fixed when the event happened.
    /// </summary>
    /// <remarks>
    /// Stored rather than rebuilt at send time: a retry an hour later has to describe the same
    /// thing the first attempt did, not whatever the item looks like by then — and it has to be
    /// byte-for-byte the same, or a receiver deduplicating on the delivery id sees two versions.
    /// </remarks>
    public string Payload { get; set; } = string.Empty;

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

    public int Attempts { get; set; }

    /// <summary>The receiver's HTTP status on the last attempt. Null when it never answered.</summary>
    public int? ResponseStatus { get; set; }

    /// <summary>What went wrong on the last attempt, briefly.</summary>
    public string? Error { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public Webhook? Webhook { get; set; }
}
