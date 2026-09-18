using System.Globalization;
using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Webhooks;
using Linkbelli.Application.Email;
using Linkbelli.Application.Observability;
using Linkbelli.Application.Services;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Sources;

public sealed class SourceRunner(
    IAppDbContext db,
    ILinkService links,
    IEnumerable<ISourceInterpreter> interpreters,
    SourceConfigSecrets secrets,
    IUserQuotaService quotas,
    ISourceScheduler scheduler,
    AppMetrics metrics,
    INotificationQueue notifications,
    IWebhookEvents webhooks,
    ILogger<SourceRunner> logger) : ISourceRunner
{
    /// <summary>
    /// The title a filter matches on. Interpreters put it in metadata rather than in the title
    /// hint, and a pattern written against what the feed says should see what the feed says.
    /// </summary>
    private static string? Title(DiscoveredLink link) =>
        link.Title ?? (link.Metadata?.TryGetValue("title", out var title) == true ? title : null);

    /// <summary>What the source said the link was published at, when it said anything.</summary>
    private static DateTimeOffset? Published(DiscoveredLink link) =>
        link.Metadata?.TryGetValue(RssSourceInterpreter.PublishedKey, out var published) == true
        && DateTimeOffset.TryParse(
            published, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    public async Task RunAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null)
        {
            return;
        }

        // Quota applies to every run (manual or scheduled — both reach here). Over-quota runs
        // are skipped silently (no run row) so they don't count against the window themselves.
        var quota = await quotas.GetOrCreateAsync(source.OwnerId, cancellationToken);
        if (await quotas.CountRunsTodayAsync(source.OwnerId, cancellationToken) >= quota.MaxRunsPerDay)
        {
            logger.LogInformation("Run skipped for source {SourceId}: daily run quota reached.", sourceId);
            return;
        }

        var run = new SourceRun { SourceId = sourceId, Status = SourceRunStatus.Running };
        db.SourceRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        // What this run put where, and whether it gave up — told to webhooks once it is saved.
        var added = new List<PlaylistItem>();
        string? stoppedWith = null;

        try
        {
            var interpreter = interpreters.FirstOrDefault(i => i.Type == source.Type)
                ?? throw new InvalidOperationException($"No interpreter for source type {source.Type}.");

            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(source.Config) ?? new();
            var config = secrets.Decrypt(source.Type, stored);

            var fetch = await interpreter.FetchAsync(config, source.State, cancellationToken);
            if (fetch.State is not null)
            {
                source.State = fetch.State; // persist ETag/cursor for the next run
            }

            // Everything a source found used to land unconditionally, which made a broad feed an
            // all-or-nothing proposition. The filter runs before the quota cap, so the cap keeps
            // the items the owner asked for rather than the first N the feed happened to list.
            var filter = SourceFilters.Deserialize(source.Filter)?.Compile();
            var now = DateTimeOffset.UtcNow;

            var accepted = filter is null
                ? fetch.Links
                : [.. fetch.Links.Where(l => filter.Accepts(l.Url, Title(l), Published(l), now))];

            var discovered = accepted
                .Take(Math.Min(filter?.Filter.MaxItems ?? int.MaxValue, quota.MaxItemsPerRun))
                .ToList();

            // Everything the run turned away, whether by pattern, by age or by the cap. Without
            // it a strict filter and a broken selector look identical from the outside.
            run.SkippedCount = fetch.Links.Count - discovered.Count;

            // Determine which candidate URLs are already known to the application before resolving,
            // so ItemsAdded reflects links genuinely new to the system (not just new to a playlist).
            var candidateHashes = new HashSet<string>(
                discovered
                    .Where(d => UrlCanonicalizer.TryCanonicalize(d.Url, out _))
                    .Select(d => { UrlCanonicalizer.TryCanonicalize(d.Url, out var c); return c.Hash; }));

            var preExistingHashes = (await db.Links
                .Where(l => candidateHashes.Contains(l.UrlHash))
                .Select(l => l.UrlHash)
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var playlistIds = await db.PlaylistSources
                .Where(ps => ps.SourceId == sourceId)
                .Select(ps => ps.PlaylistId)
                .ToListAsync(cancellationToken);

            // Resolve/dedup the links (get-or-create persists genuinely new ones, queued for
            // async enrichment). Both ItemsFound and ItemsAdded use canonical URLs so the frontend can compare them.
            var foundUrls = new List<string>();
            var resolved = new List<(Link link, bool isNew, IReadOnlyDictionary<string, string>? metadata)>();
            foreach (var discoveredLink in discovered)
            {
                if (UrlCanonicalizer.TryCanonicalize(discoveredLink.Url, out var canonical))
                {
                    try
                    {
                        var link = await links.GetOrCreateAsync(canonical, immediate: false, discoveredLink.Title, cancellationToken);
                        foundUrls.Add(link.CanonicalUrl);
                        resolved.Add((link, !preExistingHashes.Contains(canonical.Hash), discoveredLink.Metadata));
                    }
                    catch (BlockedHostException)
                    {
                        // Moderation-blocked host — silently skip this link.
                    }
                }
            }
            run.FoundCount = foundUrls.Count;
            run.ItemsFound = foundUrls.Take(SourceRun.SampleSize).ToArray();

            // ItemsAdded = URLs that were new to the application (first time seen globally).
            var seenIds = new HashSet<Guid>();
            var addedUrls = new List<string>();
            foreach (var (link, isNew, _) in resolved)
            {
                if (isNew && seenIds.Add(link.Id))
                {
                    addedUrls.Add(link.CanonicalUrl);
                }
            }
            run.AddedCount = addedUrls.Count;
            run.ItemsAdded = addedUrls.Take(SourceRun.SampleSize).ToArray();

            // Membership and next-position are read for every attached playlist in two queries,
            // both narrowed to this run's candidates. Loading a playlist's entire LinkId set to
            // test at most MaxItemsPerRun candidates meant a 10k-item playlist on a 5-minute
            // source re-read 10k ids every 5 minutes.
            var candidateLinkIds = resolved.Select(r => r.link.Id).Distinct().ToList();

            var present = (await db.PlaylistItems
                    .Where(i => playlistIds.Contains(i.PlaylistId) && candidateLinkIds.Contains(i.LinkId))
                    .Select(i => new { i.PlaylistId, i.LinkId })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.PlaylistId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.LinkId).ToHashSet());

            // Removing something a source found is otherwise temporary: the next run puts it
            // straight back, with no way to say no permanently. The window is answered from the
            // removed items themselves, which is why it can't outlast the trash they sit in.
            if (filter?.Filter.DedupeWindowDays is { } windowDays)
            {
                var cutoff = now.AddDays(-windowDays);
                var removed = await db.PlaylistItems
                    .IgnoreQueryFilters()
                    .Where(i => playlistIds.Contains(i.PlaylistId)
                        && candidateLinkIds.Contains(i.LinkId)
                        && i.DeletionTime != null
                        && i.DeletionTime >= cutoff)
                    .Select(i => new { i.PlaylistId, i.LinkId })
                    .ToListAsync(cancellationToken);

                foreach (var item in removed)
                {
                    if (!present.TryGetValue(item.PlaylistId, out var links))
                    {
                        present[item.PlaylistId] = links = [];
                    }

                    links.Add(item.LinkId);
                }
            }

            var nextPositions = (await db.PlaylistItems
                    .Where(i => playlistIds.Contains(i.PlaylistId))
                    .GroupBy(i => i.PlaylistId)
                    .Select(g => new { PlaylistId = g.Key, Max = g.Max(i => i.Position) })
                    .ToListAsync(cancellationToken))
                .ToDictionary(x => x.PlaylistId, x => x.Max);

            // Append to each attached playlist, then persist all new items in a single
            // SaveChanges (the finally).
            foreach (var playlistId in playlistIds)
            {
                var playlistLinks = present.TryGetValue(playlistId, out var existing) ? existing : [];
                var nextPosition = nextPositions.GetValueOrDefault(playlistId);

                foreach (var (link, _, metadata) in resolved)
                {
                    if (!playlistLinks.Add(link.Id))
                    {
                        continue;
                    }

                    nextPosition += PlaylistOrdering.Gap;
                    var item = new PlaylistItem
                    {
                        PlaylistId = playlistId,
                        LinkId = link.Id,
                        Position = nextPosition,
                        SourceId = sourceId,
                        Status = PlaylistItemStatus.Added,
                        Metadata = metadata is { Count: > 0 }
                            ? new Dictionary<string, string>(metadata)
                            : null,
                    };
                    db.PlaylistItems.Add(item);
                    added.Add(item);
                }
            }
            run.Status = SourceRunStatus.Succeeded;
            source.ConsecutiveFailures = 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Source run failed for {SourceId}", sourceId);
            run.Status = SourceRunStatus.Failed;
            run.Error = ex.Message;
            source.ConsecutiveFailures++;

            // A broken config fails identically on every run. Stop scheduling it rather than
            // burning the owner's daily quota on the same error until they happen to look.
            if (source.ConsecutiveFailures >= Source.FailureThreshold && source.Status == SourceStatus.Active)
            {
                source.Status = SourceStatus.Failing;
                scheduler.Unschedule(sourceId);
                logger.LogWarning(
                    "Source {SourceId} stopped after {Count} consecutive failures.",
                    sourceId, source.ConsecutiveFailures);

                // The one moment worth an email about a source: it has given up, so a playlist
                // has quietly stopped filling and nothing else in the app announces that.
                // Inside the status check, so a source that keeps failing only says so once.
                notifications.QueueSourceStopped(source.OwnerId, sourceId);
                stoppedWith = ex.Message;
            }
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
            source.LastRunAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            metrics.SourceRun(
                run.Status.ToString().ToLowerInvariant(),
                source.Type.ToString().ToLowerInvariant(),
                run.FoundCount,
                run.AddedCount,
                run.SkippedCount);

            // Only when the run's items were actually written — a run that failed part way
            // added nothing, and announcing what it meant to add would be announcing nothing.
            if (run.Status == SourceRunStatus.Succeeded && added.Count > 0)
            {
                await webhooks.ItemsAddedAsync([.. added.Select(i => i.Id)], ItemOrigin.Source, cancellationToken);
            }

            if (stoppedWith is not null)
            {
                await webhooks.SourceStoppedAsync(sourceId, stoppedWith, cancellationToken);
            }
        }
    }
}
