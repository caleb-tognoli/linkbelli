namespace Linkbelli.Core.Webhooks;

/// <summary>
/// The things a webhook can be told about.
/// </summary>
/// <remarks>
/// Named for what happened to somebody's library, not for which code path did it. A link a
/// source found and a link somebody pasted are both "items.added"; which route it came by is in
/// the payload, for the receiver that cares.
///
/// Plural where one action can touch many things. A source run that finds forty links is one
/// event with forty items, not forty requests — a chat bot posting each one separately is the
/// fastest way to get a webhook turned off by whoever has to read the channel.
/// </remarks>
public static class WebhookEvents
{
    /// <summary>Links saved into a playlist — by hand, by a source, by an import or a paste.</summary>
    public const string ItemsAdded = "items.added";

    /// <summary>Items marked finished, including by reading to the end in the reader.</summary>
    public const string ItemsFinished = "items.finished";

    /// <summary>A tag put on items, by hand or by a rule. One event per tag.</summary>
    public const string ItemsTagged = "items.tagged";

    /// <summary>A source that kept failing and has been stopped.</summary>
    public const string SourceStopped = "source.stopped";

    /// <summary>A passage marked in an article.</summary>
    public const string HighlightCreated = "highlight.created";

    /// <summary>
    /// Sent by the Test button, and to no webhook that did not ask for it. Not subscribable —
    /// it exists so somebody can see a delivery arrive before relying on one.
    /// </summary>
    public const string Ping = "ping";

    /// <summary>Everything a webhook can subscribe to.</summary>
    public static readonly IReadOnlyList<string> Subscribable =
    [
        ItemsAdded,
        ItemsFinished,
        ItemsTagged,
        SourceStopped,
        HighlightCreated,
    ];

    public static bool IsSubscribable(string name) => Subscribable.Contains(name);
}
