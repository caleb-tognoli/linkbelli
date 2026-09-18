namespace Linkbelli.Contracts;

/// <summary>A webhook as its owner sees it. The secret is never in here — only in the reply that made it.</summary>
public record WebhookResponse(
    Guid Id,
    string Url,
    string? Description,
    IReadOnlyList<string> Events,
    /// <summary>"Active", "Paused" or "Disabled".</summary>
    string Status,
    int ConsecutiveFailures,
    /// <summary>Why Linkbelli turned it off, when it did.</summary>
    string? DisabledReason,
    DateTimeOffset? LastDeliveredAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// A webhook and its signing secret, returned once — when it is made, and when the secret is
/// replaced. Like an API key, there is no way to read it back afterwards.
/// </summary>
public record WebhookWithSecretResponse(WebhookResponse Webhook, string Secret);

public record CreateWebhookRequest(string Url, IReadOnlyList<string> Events, string? Description = null);

/// <summary>
/// Changes to a webhook. Every field is optional and an omitted one is left alone.
/// </summary>
/// <param name="Active">
/// False pauses it. True turns it back on — including one Linkbelli disabled after repeated
/// failures, which also clears the failure count, since turning it on is the owner saying the
/// receiver is fixed.
/// </param>
public record UpdateWebhookRequest(
    string? Url = null,
    IReadOnlyList<string>? Events = null,
    string? Description = null,
    bool? Active = null);

/// <summary>One attempt at telling a webhook something, and how it went.</summary>
public record WebhookDeliveryResponse(
    Guid Id,
    string Event,
    /// <summary>"Pending", "Retrying", "Delivered", "Failed" or "Cancelled".</summary>
    string Status,
    int Attempts,
    int? ResponseStatus,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? DeliveredAt);

/// <summary>An event a webhook can subscribe to, with a line saying what it means.</summary>
public record WebhookEventInfo(string Name, string Description);
