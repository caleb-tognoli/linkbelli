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
    ILogger<SourceRunner> logger) : ISourceRunner
{
    public async Task RunAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null || !source.Enabled)
        {
            return;
        }

        var run = new SourceRun { SourceId = sourceId, Status = SourceRunStatus.Running };
        db.SourceRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var interpreter = interpreters.FirstOrDefault(i => i.Type == source.Type)
                ?? throw new InvalidOperationException($"No interpreter for source type {source.Type}.");

            var discovered = await interpreter.FetchAsync(source, cancellationToken);
            run.ItemsFound = discovered.Count;

            var playlistIds = await db.PlaylistSources
                .Where(ps => ps.SourceId == sourceId)
                .Select(ps => ps.PlaylistId)
                .ToListAsync(cancellationToken);

            var added = 0;
            foreach (var discoveredLink in discovered)
            {
                if (!UrlCanonicalizer.TryCanonicalize(discoveredLink.Url, out var canonical))
                {
                    continue;
                }

                var link = await links.GetOrCreateAsync(canonical, cancellationToken);

                foreach (var playlistId in playlistIds)
                {
                    if (await db.PlaylistItems.AnyAsync(i => i.PlaylistId == playlistId && i.LinkId == link.Id, cancellationToken))
                    {
                        continue;
                    }

                    var maxPos = await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
                        .MaxAsync(i => (long?)i.Position, cancellationToken) ?? 0;

                    db.PlaylistItems.Add(new PlaylistItem
                    {
                        PlaylistId = playlistId,
                        LinkId = link.Id,
                        Position = maxPos + PlaylistOrdering.Gap,
                        SourceId = sourceId,
                        Status = PlaylistItemStatus.Active,
                    });
                    added++;
                }

                await db.SaveChangesAsync(cancellationToken);
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
