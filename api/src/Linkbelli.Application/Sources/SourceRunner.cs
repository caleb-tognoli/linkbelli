using System.Text.Json;
using Linkbelli.Application.Data;
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
    ILogger<SourceRunner> logger) : ISourceRunner
{
    public async Task RunAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null || !source.Enabled)
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

            var discovered = fetch.Links
                .Take(quota.MaxItemsPerRun)
                .ToList();
            run.ItemsFound = discovered.Count;

            var playlistIds = await db.PlaylistSources
                .Where(ps => ps.SourceId == sourceId)
                .Select(ps => ps.PlaylistId)
                .ToListAsync(cancellationToken);

            // Resolve/dedup the links first (get-or-create persists genuinely new ones).
            var resolved = new List<Link>();
            foreach (var discoveredLink in discovered)
            {
                if (UrlCanonicalizer.TryCanonicalize(discoveredLink.Url, out var canonical))
                {
                    resolved.Add(await links.GetOrCreateAsync(canonical, cancellationToken));
                }
            }

            // Append to each playlist in memory (existing link ids + next position preloaded
            // once per playlist), then persist all new items in a single SaveChanges (the finally).
            var added = 0;
            foreach (var playlistId in playlistIds)
            {
                var present = (await db.PlaylistItems
                        .Where(i => i.PlaylistId == playlistId)
                        .Select(i => i.LinkId)
                        .ToListAsync(cancellationToken))
                    .ToHashSet();
                var nextPosition = await db.PlaylistItems
                    .Where(i => i.PlaylistId == playlistId)
                    .MaxAsync(i => (long?)i.Position, cancellationToken) ?? 0;

                foreach (var link in resolved)
                {
                    if (!present.Add(link.Id)) // also dedups repeats within this run
                    {
                        continue;
                    }

                    nextPosition += PlaylistOrdering.Gap;
                    db.PlaylistItems.Add(new PlaylistItem
                    {
                        PlaylistId = playlistId,
                        LinkId = link.Id,
                        Position = nextPosition,
                        SourceId = sourceId,
                        Status = PlaylistItemStatus.Active,
                    });
                    added++;
                }
            }

            run.ItemsAdded = added;
            run.Status = SourceRunStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Source run failed for {SourceId}", sourceId);
            run.Status = SourceRunStatus.Failed;
            run.Error = ex.Message;
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
            source.LastRunAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
