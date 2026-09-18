using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Services;
using Linkbelli.Application.Webhooks;
using Linkbelli.Core.Automation;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Automation;

/// <summary>
/// Runs a person's rules over items as they arrive. Everything a source finds lands wherever the
/// source was pointed and stays there, so filing, tagging or dismissing it is a decision made
/// again for every single item.
/// </summary>
public interface IAutomationRunner
{
    /// <summary>
    /// Applies the rules to any of these items that haven't been through them yet. Items whose
    /// link is still being enriched are left for the sweep, because a rule about a title can't
    /// be judged before there is one.
    /// </summary>
    Task<int> ApplyAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs one saved rule over links already in the library, optionally narrowed to a playlist.
    /// Returns how many items it acted on.
    /// </summary>
    Task<int> ApplyRuleAsync(
        Guid ownerId, Guid ruleId, Guid? playlistId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks up everything still waiting and applies the rules to it. This is what makes the
    /// feature correct: items arrive by several routes and are enriched well after they land.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AutomationRunner(
    IAppDbContext db,
    ITagResolver tags,
    IWebhookEvents webhooks,
    ILogger<AutomationRunner> logger) : IAutomationRunner
{
    /// <summary>Items per sweep. Enough to keep up with a large source run without a long lock.</summary>
    public const int BatchSize = 200;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var itemIds = await db.PlaylistItems
            .Where(i => i.AutomationAppliedAt == null && i.Link!.EnrichedAt != null)
            .OrderBy(i => i.CreationTime)
            .Take(BatchSize)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        return itemIds.Count == 0 ? 0 : await ApplyAsync(itemIds, cancellationToken);
    }

    /// <summary>
    /// Runs one saved rule over links that are already here.
    /// </summary>
    /// <remarks>
    /// Rules only ever saw what arrived after they were written, which the Rules page said out
    /// loud — and which is backwards, because you write a rule about a pattern you noticed in the
    /// library you already have.
    ///
    /// Deliberately not implemented by clearing AutomationAppliedAt and letting the sweep pick
    /// the items up: that would run <em>every</em> rule over them again, and a rule with
    /// CopyToPlaylistId would copy a second time. This applies exactly the rule that was asked
    /// for, and leaves the stamp alone.
    /// </remarks>
    public async Task<int> ApplyRuleAsync(
        Guid ownerId, Guid ruleId, Guid? playlistId, CancellationToken cancellationToken = default)
    {
        var rule = await db.AutomationRules
            .FirstOrDefaultAsync(r => r.Id == ruleId && r.OwnerId == ownerId, cancellationToken)
            ?? throw new NotFoundException("Rule not found.");

        var scope = db.PlaylistItems
            .Include(i => i.Link).ThenInclude(l => l!.Host)
            .Include(i => i.Playlist)
            .Include(i => i.Tags)
            .Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);

        // The rule's own scope still applies; asking for a playlist narrows it further.
        if (rule.PlaylistId is { } ruleScope)
        {
            scope = scope.Where(i => i.PlaylistId == ruleScope);
        }

        if (playlistId is { } asked)
        {
            scope = scope.Where(i => i.PlaylistId == asked);
        }

        var items = await scope.Take(BacklogLimit).ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return 0;
        }

        var compiled = new CompiledRule(rule);
        var now = DateTimeOffset.UtcNow;
        var acted = 0;
        var effects = new RunEffects();

        foreach (var item in items)
        {
            var candidate = new RuleCandidate(
                item.PlaylistId,
                item.Link!.CanonicalUrl,
                item.Link.Host?.Hostname ?? string.Empty,
                item.Metadata?.GetValueOrDefault("title") ?? item.Link.Title,
                item.Link.Kind,
                item.Link.WordCount,
                item.Link.EnrichmentStatus == EnrichmentStatus.Broken,
                item.SourceId);

            if (!compiled.Matches(candidate))
            {
                continue;
            }

            await ActAsync(item, rule, effects, cancellationToken);
            rule.MatchCount++;
            rule.LastMatchedAt = now;
            acted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await effects.PublishAsync(webhooks, cancellationToken);

        logger.LogInformation(
            "Rule {RuleId} run over {Count} existing items; acted on {Acted}.", ruleId, items.Count, acted);

        return acted;
    }

    /// <summary>
    /// How much of an existing library one run will look at.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than a page: this is a deliberate, occasional action, and the actions a
    /// rule can take include trashing and moving. Doing ten thousand of those in one request
    /// without the person seeing any of it is not something to offer.
    /// </remarks>
    public const int BacklogLimit = 2_000;

    public async Task<int> ApplyAsync(IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        if (itemIds.Count == 0)
        {
            return 0;
        }

        var items = await db.PlaylistItems
            .Include(i => i.Link).ThenInclude(l => l!.Host)
            .Include(i => i.Playlist)
            .Include(i => i.Tags)
            .Where(i => itemIds.Contains(i.Id)
                && i.AutomationAppliedAt == null
                && i.Link!.EnrichedAt != null)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return 0;
        }

        // Rules belong to the owner of the playlist the item is in, and one batch can span
        // several owners when it comes from the sweep.
        var ownerIds = items.Select(i => i.Playlist!.OwnerId).Distinct().ToList();
        var rulesByOwner = (await db.AutomationRules
                .Where(r => ownerIds.Contains(r.OwnerId) && r.Enabled)
                .OrderBy(r => r.Position)
                .ThenBy(r => r.CreationTime)
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.OwnerId)
            .ToDictionary(g => g.Key, g => g.Select(r => new CompiledRule(r)).ToList());

        var now = DateTimeOffset.UtcNow;
        var acted = 0;
        var effects = new RunEffects();

        foreach (var item in items)
        {
            item.AutomationAppliedAt = now;

            if (!rulesByOwner.TryGetValue(item.Playlist!.OwnerId, out var rules))
            {
                continue;
            }

            if (await RunRulesAsync(item, rules, now, effects, cancellationToken))
            {
                acted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await effects.PublishAsync(webhooks, cancellationToken);

        if (acted > 0)
        {
            logger.LogInformation("Automation acted on {Acted} of {Count} items.", acted, items.Count);
        }

        return acted;
    }

    /// <summary>Runs one item past the rules in order. Returns whether anything happened to it.</summary>
    private async Task<bool> RunRulesAsync(
        PlaylistItem item, List<CompiledRule> rules, DateTimeOffset now, RunEffects effects, CancellationToken ct)
    {
        var candidate = new RuleCandidate(
            item.PlaylistId,
            item.Link!.CanonicalUrl,
            item.Link.Host?.Hostname ?? string.Empty,
            item.Metadata?.GetValueOrDefault("title") ?? item.Link.Title,
            item.Link.Kind,
            item.Link.WordCount,
            item.Link.EnrichmentStatus == EnrichmentStatus.Broken,
            item.SourceId);

        var acted = false;

        foreach (var rule in rules)
        {
            if (!rule.Matches(candidate))
            {
                continue;
            }

            await ActAsync(item, rule.Rule, effects, ct);

            rule.Rule.MatchCount++;
            rule.Rule.LastMatchedAt = now;
            acted = true;

            if (rule.Rule.StopOnMatch)
            {
                break;
            }

            // A rule that moved the item changes what the rules after it are looking at, so the
            // candidate follows it rather than describing where it used to be.
            candidate = candidate with { PlaylistId = item.PlaylistId };

            if (item.DeletionTime is not null)
            {
                // Trashed. Nothing below should keep filing something the owner said no to.
                break;
            }
        }

        return acted;
    }

    private async Task ActAsync(PlaylistItem item, AutomationRule rule, RunEffects effects, CancellationToken ct)
    {
        if (rule.AddTags.Length > 0)
        {
            await AddTagsAsync(item, rule.AddTags, effects, ct);
        }

        if (rule.MarkWatched && item.Status != PlaylistItemStatus.Watched)
        {
            item.Status = PlaylistItemStatus.Watched;
            item.StatusChangedAt = DateTimeOffset.UtcNow;
            effects.Finished.Add(item.Id);
        }

        if (rule.SetScore is { } score)
        {
            // The queue sorts on score, so this is how a rule says "this source is worth my
            // time" without anybody rating a single item by hand.
            item.Score = Math.Clamp(score, 0, 100);
        }

        if (rule.Archive && item.Link is not null)
        {
            // Asked for, not done here: archiving is an outbound request to somebody else's
            // server, and the sweep that already exists is where the rate limiting and the
            // retry budget live.
            item.Link.ArchiveRequested = true;
        }

        if (rule.CopyToPlaylistId is { } copyTo)
        {
            await CopyAsync(item, copyTo, effects, ct);
        }

        if (rule.MoveToPlaylistId is { } moveTo && moveTo != item.PlaylistId)
        {
            await MoveAsync(item, moveTo, effects, ct);
        }

        if (rule.Trash)
        {
            db.PlaylistItems.Remove(item); // soft delete, so it is recoverable from the trash
        }
    }

    private async Task AddTagsAsync(PlaylistItem item, string[] names, RunEffects effects, CancellationToken ct)
    {
        var resolved = await tags.ResolveAsync(names, ct);
        var existing = item.Tags.Select(t => t.TagId).ToHashSet();

        foreach (var tag in resolved.Where(t => !existing.Contains(t.Id)))
        {
            item.Tags.Add(new PlaylistItemTag { PlaylistItemId = item.Id, TagId = tag.Id });
            effects.Tagged(tag.Name).Add(item.Id);
        }

        // Tags live in join rows, so nothing on the item itself changes — without this a sync
        // client would never hear that it was tagged.
        item.LastModified = DateTimeOffset.UtcNow;
    }

    private async Task MoveAsync(PlaylistItem item, Guid playlistId, RunEffects effects, CancellationToken ct)
    {
        if (!await OwnedAsync(item, playlistId, ct))
        {
            return;
        }

        // Already there by another route: move it nowhere rather than creating a duplicate.
        if (await db.PlaylistItems.AnyAsync(
            i => i.PlaylistId == playlistId && i.LinkId == item.LinkId && i.Id != item.Id, ct))
        {
            db.PlaylistItems.Remove(item);
            return;
        }

        item.PlaylistId = playlistId;
        item.Position = await NextPositionAsync(playlistId, ct);
        effects.Arrived.Add(item.Id);
    }

    private async Task CopyAsync(PlaylistItem item, Guid playlistId, RunEffects effects, CancellationToken ct)
    {
        if (playlistId == item.PlaylistId || !await OwnedAsync(item, playlistId, ct))
        {
            return;
        }

        if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == playlistId && i.LinkId == item.LinkId, ct))
        {
            return;
        }

        var copy = new PlaylistItem
        {
            PlaylistId = playlistId,
            LinkId = item.LinkId,
            Position = await NextPositionAsync(playlistId, ct),
            SourceId = item.SourceId,
            Note = item.Note,
            Metadata = item.Metadata is null ? null : new Dictionary<string, string>(item.Metadata),
            // Already been past the rules: it was put here by one, and running them over it
            // again is how two rules pointing at each other become a loop.
            AutomationAppliedAt = DateTimeOffset.UtcNow,
        };

        db.PlaylistItems.Add(copy);
        effects.Arrived.Add(copy.Id);
    }

    /// <summary>
    /// What a run did that webhooks hear about, gathered as it goes and told once it is saved.
    /// </summary>
    /// <remarks>
    /// Told afterwards rather than as each rule acts, because until the save nothing has
    /// happened — and a later rule in the same run may trash the item, which the publisher then
    /// finds gone and leaves out, rather than announcing a link that is no longer there.
    /// </remarks>
    private sealed class RunEffects
    {
        private readonly Dictionary<string, List<Guid>> _tagged = new(StringComparer.Ordinal);

        public List<Guid> Finished { get; } = [];

        /// <summary>Filed into another playlist, by a copy or a move.</summary>
        public List<Guid> Arrived { get; } = [];

        public List<Guid> Tagged(string tag) =>
            _tagged.TryGetValue(tag, out var ids) ? ids : _tagged[tag] = [];

        public async Task PublishAsync(IWebhookEvents webhooks, CancellationToken ct)
        {
            await webhooks.ItemsAddedAsync(Arrived, ItemOrigin.Rule, ct);
            await webhooks.ItemsFinishedAsync(Finished, ct);

            foreach (var (tag, ids) in _tagged)
            {
                await webhooks.ItemsTaggedAsync(ids, tag, ct);
            }
        }
    }

    /// <summary>
    /// Whether the destination belongs to the same person. Rules are checked when they are saved,
    /// but a playlist can be deleted or handed over afterwards, and a rule is not a licence to
    /// write into whatever it happens to name.
    /// </summary>
    private async Task<bool> OwnedAsync(PlaylistItem item, Guid playlistId, CancellationToken ct)
    {
        var owned = await db.Playlists.AnyAsync(
            p => p.Id == playlistId && p.OwnerId == item.Playlist!.OwnerId, ct);

        if (!owned)
        {
            logger.LogWarning(
                "Automation skipped a destination playlist {PlaylistId} that is no longer the owner's.",
                playlistId);
        }

        return owned;
    }

    private async Task<long> NextPositionAsync(Guid playlistId, CancellationToken ct)
    {
        var max = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position, ct) ?? 0;

        return max + PlaylistOrdering.Gap;
    }
}
