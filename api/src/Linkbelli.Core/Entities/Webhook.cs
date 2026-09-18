namespace Linkbelli.Core.Entities;

public enum WebhookStatus
{
    Active = 0,

    /// <summary>Turned off by its owner.</summary>
    Paused = 1,

    /// <summary>
    /// Turned off by Linkbelli after deliveries kept failing, or after the receiver answered
    /// 410 Gone. Re-enabling it is the owner's call, and clears the count.
    /// </summary>
    Disabled = 2,
}

/// <summary>
/// An address Linkbelli tells about things that happen in somebody's library.
/// </summary>
/// <remarks>
/// Everything else about this app was built to be written to — a feed, a scraper, an inbox
/// address, an email worker — and nothing could be told what happened next. "A link was saved",
/// "a source stopped", "something was tagged to-read" are exactly what people hang a Home
/// Assistant automation, a chat bot, an n8n flow or a site rebuild off.
///
/// The address is supplied by the user and fetched by the server, so every delivery goes
/// through the same SSRF guard that enrichment does. It is a cleaner request-forgery primitive
/// than anything else in the app — the caller chooses the address and the body is predictable —
/// which is why the guard is not optional and why only an operator can widen it.
/// </remarks>
public class Webhook : BaseEntity<Guid>
{
    /// <summary>Failed deliveries in a row before Linkbelli stops trying.</summary>
    /// <remarks>
    /// Each delivery already retries on its own, so this counts deliveries that gave up, not
    /// attempts — five of those is hours of a receiver being down, and a dead endpoint that keeps
    /// being called is noise in somebody else's logs.
    /// </remarks>
    public const int FailureThreshold = 5;

    /// <summary>How many one account may have.</summary>
    public const int MaxPerOwner = 10;

    public const int MaxUrlLength = 2048;

    public const int MaxDescriptionLength = 200;

    public Guid OwnerId { get; set; }

    public string Url { get; set; } = string.Empty;

    /// <summary>What it is for, in the owner's words. Only ever shown back to them.</summary>
    public string? Description { get; set; }

    /// <summary>The events it is sent, by name — see <c>WebhookEvents</c>.</summary>
    public string[] Events { get; set; } = [];

    /// <summary>
    /// The signing secret, encrypted at rest.
    /// </summary>
    /// <remarks>
    /// Kept retrievably, unlike an API key, because the server has to use it on every delivery
    /// rather than only compare against it. Shown to the owner once, when it is made.
    /// </remarks>
    public string ProtectedSecret { get; set; } = string.Empty;

    public WebhookStatus Status { get; set; } = WebhookStatus.Active;

    /// <summary>Deliveries that gave up, in a row. Reset by any success.</summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>Why Linkbelli turned it off, when it did.</summary>
    public string? DisabledReason { get; set; }

    public DateTimeOffset? LastDeliveredAt { get; set; }
}
