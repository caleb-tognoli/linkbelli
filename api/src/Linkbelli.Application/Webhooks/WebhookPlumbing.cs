namespace Linkbelli.Application.Webhooks;

/// <summary>Instance-wide webhook settings, from the <c>Webhooks</c> configuration section.</summary>
public sealed class WebhookOptions
{
    public const string Section = "Webhooks";

    /// <summary>
    /// Let deliveries reach private network addresses (10/8, 172.16/12, 192.168/16, fc00::/7).
    /// </summary>
    /// <remarks>
    /// Off by default, and an operator's decision rather than a user's: on a shared instance it
    /// would let any account make this server send requests into the network it sits on. On a
    /// home server it is usually exactly what is wanted — the receiver is a box on the same
    /// LAN. Loopback and link-local stay refused either way.
    /// </remarks>
    public bool AllowPrivateNetworks { get; set; }
}

/// <summary>Durable delivery — Hangfire in the app, a recorder in the tests.</summary>
public interface IWebhookQueue
{
    void Enqueue(Guid deliveryId);

    void Schedule(Guid deliveryId, TimeSpan delay);
}

/// <summary>The HTTP client deliveries are sent through.</summary>
public static class WebhookHttpClient
{
    public const string Name = "webhooks";

    /// <summary>
    /// How long a receiver has to answer. A webhook receiver's whole job is to say "got it" and
    /// do its work afterwards; one that takes longer than this is going to be retried anyway.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public const string UserAgent = "Linkbelli-Webhooks/1.0";
}
