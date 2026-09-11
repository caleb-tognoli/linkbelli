using Linkbelli.Application.Data;
using Linkbelli.Application.Services;
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
    /// Picks up everything still waiting and applies the rules to it. This is what makes the
    /// feature correct: items arrive by several routes and are enriched well after they land.
    /// </summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AutomationRunner(
    IAppDbContext db,
    ITagResolver tags,
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

        foreach (var item in items)
        {
            item.AutomationAppliedAt = now;

            if (!rulesByOwner.TryGetValue(item.Playlist!.OwnerId, out var rules))
            {
                continue;
            }

            if (await RunRulesAsync(item, rules, now, cancellationToken))
            {
                acted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (acted > 0)
        {
            logger.LogInformation("Automation acted on {Acted} of {Count} items.", acted, items.Count);
        }

        return acted;
    }

    /// <summary>Runs one item past the rules in order. Returns whether anything happened to it.</summary>
    private async Task<bool> RunRulesAsync(
        PlaylistItem item, List<CompiledRule> rules, DateTimeOffset now, CancellationToken ct)
    {
        var candidate = new RuleCandidate(
            item.PlaylistId,
            item.Link!.CanonicalUrl,
            item.Link.Host?.Hostname ?? string.Empty,
            item.Metadata?.GetValueOrDefault("title") ?? item.Link.Title,
            item.Link.Kind);

        var acted = false;

        foreach (var rule in rules)
        {
            if (!rule.Matches(candidate))
            {
                continue;
            }

            await ActAsync(item, rule.Rule, ct);

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

    private async Task ActAsync(PlaylistItem item, AutomationRule rule, CancellationToken ct)
    {
        if (rule.AddTags.Length > 0)
        {
            await AddTagsAsync(item, rule.AddTags, ct);
        }

        if (rule.MarkWatched && item.Status != PlaylistItemStatus.Watched)
        {
            item.Status = PlaylistItemStatus.Watched;
            item.StatusChangedAt = DateTimeOffset.UtcNow;
        }

        if (rule.CopyToPlaylistId is { } copyTo)
        {
            await CopyAsync(item, copyTo, ct);
        }

        if (rule.MoveToPlaylistId is { } moveTo && moveTo != item.PlaylistId)
        {
            await MoveAsync(item, moveTo, ct);
        }

        if (rule.Trash)
        {
            db.PlaylistItems.Remove(item); // soft delete, so it is recoverable from the trash
        }
    }

    private async Task AddTagsAsync(PlaylistItem item, string[] names, CancellationToken ct)
    {
        var resolved = await tags.ResolveAsync(names, ct);
        var existing = item.Tags.Select(t => t.TagId).ToHashSet();

        foreach (var tag in resolved.Where(t => !existing.Contains(t.Id)))
        {
            item.Tags.Add(new PlaylistItemTag { PlaylistItemId = item.Id, TagId = tag.Id });
        }

        // Tags live in join rows, so nothing on the item itself changes — without this a sync
        // client would never hear that it was tagged.
        item.LastModified = DateTimeOffset.UtcNow;
    }

    private async Task MoveAsync(PlaylistItem item, Guid playlistId, CancellationToken ct)
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
    }

    private async Task CopyAsync(PlaylistItem item, Guid playlistId, CancellationToken ct)
    {
        if (playlistId == item.PlaylistId || !await OwnedAsync(item, playlistId, ct))
        {
            return;
        }

        if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == playlistId && i.LinkId == item.LinkId, ct))
        {
            return;
        }

        db.PlaylistItems.Add(new PlaylistItem
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
        });
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
