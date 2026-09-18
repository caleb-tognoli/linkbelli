using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Webhooks;

/// <summary>How a batch of items arrived, as it is named in an <c>items.added</c> payload.</summary>
public static class ItemOrigin
{
    public const string Manual = "manual";
    public const string Source = "source";
    public const string Import = "import";
    public const string Paste = "paste";

    /// <summary>Filed into this playlist by one of the owner's rules.</summary>
    public const string Rule = "rule";
}

/// <summary>
/// Tells webhooks what happened. Call it after the change has been saved.
/// </summary>
/// <remarks>
/// Every method is safe to call from the middle of anything: none of them throws, and all of
/// them are a single indexed query and nothing else when nobody is subscribed, which on most
/// instances is always. A link that was saved stays saved whatever happens to the webhook
/// bookkeeping about it.
/// </remarks>
public interface IWebhookEvents
{
    Task ItemsAddedAsync(IReadOnlyCollection<Guid> itemIds, string via, CancellationToken ct = default);

    Task ItemsFinishedAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);

    Task ItemsTaggedAsync(IReadOnlyCollection<Guid> itemIds, string tag, CancellationToken ct = default);

    Task SourceStoppedAsync(Guid sourceId, string? lastError, CancellationToken ct = default);

    Task HighlightCreatedAsync(Guid highlightId, CancellationToken ct = default);

    /// <summary>A test delivery to one webhook, whatever it is subscribed to.</summary>
    Task<Guid?> PingAsync(Guid webhookId, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// Works in a scope of its own rather than on the caller's DbContext. Saving the deliveries on
/// the caller's context would also save whatever the caller had pending, and a failure here
/// would leave half-added rows behind for the caller's next save to trip over. A separate
/// context sees only what has been committed, which is the other reason these are called after
/// the caller's own save.
/// </remarks>
public sealed class WebhookPublisher(
    IServiceScopeFactory scopes,
    IWebhookQueue queue,
    ILogger<WebhookPublisher> logger) : IWebhookEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task ItemsAddedAsync(IReadOnlyCollection<Guid> itemIds, string via, CancellationToken ct = default) =>
        ItemsEventAsync(WebhookEvents.ItemsAdded, itemIds, (group, items) => new
        {
            via,
            playlist = group.Playlist,
            // The source that found them, when one did. All items in a group share a playlist,
            // and a source run writes a batch per playlist, so one is enough to name it.
            source = group.Source,
            items,
        }, ct);

    public Task ItemsFinishedAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        ItemsEventAsync(WebhookEvents.ItemsFinished, itemIds, (group, items) => new
        {
            playlist = group.Playlist,
            items,
        }, ct);

    public Task ItemsTaggedAsync(IReadOnlyCollection<Guid> itemIds, string tag, CancellationToken ct = default) =>
        ItemsEventAsync(WebhookEvents.ItemsTagged, itemIds, (group, items) => new
        {
            tag,
            playlist = group.Playlist,
            items,
        }, ct);

    public Task SourceStoppedAsync(Guid sourceId, string? lastError, CancellationToken ct = default) =>
        SafelyAsync(WebhookEvents.SourceStopped, async db =>
        {
            var source = await db.Sources
                .AsNoTracking()
                .Where(s => s.Id == sourceId)
                .Select(s => new { s.Id, s.OwnerId, s.Name, s.Type, s.ConsecutiveFailures })
                .FirstOrDefaultAsync(ct);

            if (source is null)
            {
                return [];
            }

            var hooks = await SubscribedAsync(db, WebhookEvents.SourceStopped, [source.OwnerId], ct);

            return Stage(db, hooks, WebhookEvents.SourceStopped, new
            {
                source = new { source.Id, source.Name, type = source.Type.ToString() },
                source.ConsecutiveFailures,
                lastError,
            });
        }, ct);

    public Task HighlightCreatedAsync(Guid highlightId, CancellationToken ct = default) =>
        SafelyAsync(WebhookEvents.HighlightCreated, async db =>
        {
            var row = await db.Highlights
                .AsNoTracking()
                .Where(h => h.Id == highlightId)
                .Select(h => new
                {
                    h.OwnerId,
                    link = new { id = h.LinkId, url = h.Link!.CanonicalUrl, title = h.Link.Title },
                    highlight = new { h.Id, h.Text, h.Note, h.ParagraphIndex },
                })
                .FirstOrDefaultAsync(ct);

            if (row is null)
            {
                return [];
            }

            var hooks = await SubscribedAsync(db, WebhookEvents.HighlightCreated, [row.OwnerId], ct);

            return Stage(db, hooks, WebhookEvents.HighlightCreated, new { row.link, row.highlight });
        }, ct);

    public async Task<Guid?> PingAsync(Guid webhookId, CancellationToken ct = default)
    {
        var staged = await SafelyAsync(WebhookEvents.Ping, db =>
            Task.FromResult(Stage(db, [webhookId], WebhookEvents.Ping, new
            {
                message = "A test from Linkbelli. If this arrived, the address works — "
                    + "check the signature before trusting anything else it sends.",
            })), ct);

        return staged.Count == 1 ? staged[0] : null;
    }

    /// <summary>One item-shaped event per playlist the items are in.</summary>
    /// <remarks>
    /// Per playlist rather than per call, because the playlist is the thing a receiver routes
    /// on — "post new links in Reading to this channel" — and a bulk action across three lists
    /// is, to anybody downstream, three separate things that happened.
    ///
    /// Fired for the playlist's owner, whoever did it. An editor adding to somebody else's list
    /// is adding to that person's library, and it is their automations that should hear.
    /// </remarks>
    private Task ItemsEventAsync(
        string eventName,
        IReadOnlyCollection<Guid> itemIds,
        Func<ItemGroup, IReadOnlyList<ItemData>, object> describe,
        CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return SafelyAsync(eventName, async db =>
        {
            var ids = itemIds.Distinct().ToList();

            // Asked before anything is loaded: on most instances nobody has a webhook, and this
            // is the only query that should cost them anything.
            var hooks = await db.Webhooks
                .AsNoTracking()
                .Where(w => w.Status == WebhookStatus.Active
                    && w.Events.Contains(eventName)
                    && db.PlaylistItems.Any(i => ids.Contains(i.Id) && i.Playlist!.OwnerId == w.OwnerId))
                .Select(w => new Hook(w.Id, w.OwnerId))
                .ToListAsync(ct);

            if (hooks.Count == 0)
            {
                return [];
            }

            var rows = await db.PlaylistItems
                .AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .OrderBy(i => i.Position)
                .Select(i => new
                {
                    i.Playlist!.OwnerId,
                    Playlist = new PlaylistData(i.PlaylistId, i.Playlist.Name, i.Playlist.Slug),
                    Source = i.SourceId == null ? null : new SourceData(i.SourceId.Value, i.Source!.Name),
                    Item = new ItemData(
                        i.Id, i.LinkId, i.Link!.CanonicalUrl, i.Link.Title, i.Note, i.Status.ToString(), i.CreationTime),
                })
                .ToListAsync(ct);

            var staged = new List<Guid>();
            foreach (var group in rows.GroupBy(r => r.Playlist.Id))
            {
                var first = group.First();
                var owners = hooks.Where(h => h.OwnerId == first.OwnerId).Select(h => h.Id).ToList();

                staged.AddRange(Stage(
                    db,
                    owners,
                    eventName,
                    describe(new ItemGroup(first.Playlist, first.Source), [.. group.Select(g => g.Item)])));
            }

            return staged;
        }, ct);
    }

    private static async Task<List<Guid>> SubscribedAsync(
        IAppDbContext db, string eventName, IReadOnlyCollection<Guid> owners, CancellationToken ct) =>
        await db.Webhooks
            .AsNoTracking()
            .Where(w => w.Status == WebhookStatus.Active && w.Events.Contains(eventName) && owners.Contains(w.OwnerId))
            .Select(w => w.Id)
            .ToListAsync(ct);

    /// <summary>A delivery row per webhook, each with the body it will be sent, byte for byte.</summary>
    private static List<Guid> Stage(IAppDbContext db, IEnumerable<Guid> webhookIds, string eventName, object data)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var staged = new List<Guid>();

        foreach (var webhookId in webhookIds)
        {
            var id = Guid.CreateVersion7();
            db.WebhookDeliveries.Add(new WebhookDelivery
            {
                Id = id,
                WebhookId = webhookId,
                Event = eventName,
                // The delivery id is in the body so a receiver can drop a retry it already
                // handled — the one thing every webhook receiver eventually has to do.
                Payload = JsonSerializer.Serialize(new { id, @event = eventName, occurredAt, data }, Json),
            });
            staged.Add(id);
        }

        return staged;
    }

    private async Task<List<Guid>> SafelyAsync(
        string eventName, Func<IAppDbContext, Task<List<Guid>>> work, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var staged = await work(db);
            if (staged.Count == 0)
            {
                return staged;
            }

            await db.SaveChangesAsync(ct);

            // Queued only once the rows exist, or a fast worker could go looking for a delivery
            // that has not been written yet.
            foreach (var id in staged)
            {
                queue.Enqueue(id);
            }

            return staged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never the caller's problem. The link was saved; failing to announce it is logged
            // and nothing more.
            logger.LogError(ex, "Could not queue {Event} webhook deliveries.", eventName);
            return [];
        }
    }

    private sealed record Hook(Guid Id, Guid OwnerId);

    private sealed record ItemGroup(PlaylistData Playlist, SourceData? Source);

    private sealed record PlaylistData(Guid Id, string Name, string Slug);

    private sealed record SourceData(Guid Id, string Name);

    private sealed record ItemData(
        Guid Id, Guid LinkId, string Url, string? Title, string? Note, string Status, DateTimeOffset AddedAt);
}
