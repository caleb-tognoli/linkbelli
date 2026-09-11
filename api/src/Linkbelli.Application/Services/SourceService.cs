using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class SourceService(
    IAppDbContext db,
    IEnumerable<ISourceInterpreter> interpreters,
    SourceConfigSecrets secrets,
    ISourceScheduler scheduler,
    ISourceTemplateService templates,
    IUserQuotaService quotas) : ISourceService
{
    private const int PreviewLimit = 10;

    public async Task<IReadOnlyList<SourceResponse>> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        // Fetch sources with their latest run status. Sort: never-run first, then failures
        // (most-recent first), then successes (most-recent first).
        var sourcesWithStatus = await db.Sources
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .Select(s => new
            {
                Source = s,
                LastRunStatus = db.SourceRuns
                    .Where(r => r.SourceId == s.Id)
                    .OrderByDescending(r => r.CreationTime)
                    .Select(r => (SourceRunStatus?)r.Status)
                    .FirstOrDefault()
            })
            .OrderBy(x => x.Source.LastRunAt == null ? 0 : (x.LastRunStatus == SourceRunStatus.Failed ? 1 : 2))
            .ThenByDescending(x => x.Source.LastRunAt)
            .ToListAsync(ct);

        // Fetch every source's playlist attachments in one grouped query instead of N round-trips.
        var sourceIds = sourcesWithStatus.Select(x => x.Source.Id).ToList();
        var playlistIdsBySource = (await db.PlaylistSources
                .Where(ps => sourceIds.Contains(ps.SourceId))
                .Select(ps => new { ps.SourceId, ps.PlaylistId })
                .ToListAsync(ct))
            .GroupBy(ps => ps.SourceId)
            .ToDictionary(g => g.Key, g => g.Select(ps => ps.PlaylistId).ToArray());

        return sourcesWithStatus
            .Select(x => ToResponse(
                x.Source,
                playlistIdsBySource.TryGetValue(x.Source.Id, out var ids) ? ids : [],
                x.LastRunStatus))
            .ToList();
    }

    public async Task<SourceResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        return ToResponse(source, await PlaylistIdsAsync(id, ct));
    }

    public async Task<SourceResponse> CreateAsync(Guid ownerId, CreateSourceRequest request, CancellationToken ct = default)
    {
        // A template supplies the config, so the person only ever fills in what is genuinely
        // theirs. The rendered result still goes through the interpreter's own validation below:
        // a template can't talk the app into accepting a config it otherwise wouldn't.
        if (request.TemplateId is { } templateId)
        {
            var (type, config, suggested) = await templates.RenderAsync(
                templateId, request.Variables ?? new Dictionary<string, string>(), ct);

            request = request with
            {
                Type = type,
                Config = config,
                Schedule = string.IsNullOrWhiteSpace(request.Schedule)
                    ? suggested ?? "0 * * * *"
                    : request.Schedule,
            };
        }

        var interpreter = ResolveInterpreter(request.Type);
        Validate(request.Name, request.Schedule, request.Config, interpreter);
        await EnsurePlaylistsOwnedAsync(ownerId, request.PlaylistIds, ct);
        await quotas.EnsureCanCreateSourceAsync(ownerId, ct);

        var source = new Source
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Type = request.Type,
            Config = JsonSerializer.Serialize(secrets.Encrypt(request.Type, request.Config, stored: null)),
            Schedule = request.Schedule.Trim(),
            Visibility = request.Visibility ?? SourceVisibility.Private,
            TimeZone = NormalizeTimeZone(request.TimeZone),
            Filter = SourceFilters.Serialize(SourceFilters.Normalize(request.Filter)),
        };
        db.Sources.Add(source);

        foreach (var playlistId in request.PlaylistIds ?? [])
        {
            db.PlaylistSources.Add(new PlaylistSource { SourceId = source.Id, PlaylistId = playlistId });
        }

        await db.SaveChangesAsync(ct);
        scheduler.Schedule(source.Id, source.Schedule, source.TimeZone);

        return ToResponse(source, (request.PlaylistIds ?? []).ToArray());
    }

    public async Task<SourceResponse> UpdateAsync(Guid ownerId, Guid id, UpdateSourceRequest request, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        var interpreter = ResolveInterpreter(source.Type);

        if (request.Type is not null && request.Type.Value != source.Type)
        {
            source.Type = request.Type.Value;
            interpreter = ResolveInterpreter(source.Type);
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new ValidationException("name", "Name cannot be empty.");
            }

            source.Name = request.Name.Trim();
        }

        if (request.Config is not null)
        {
            interpreter.ValidateConfig(request.Config);
            var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(source.Config) ?? new();
            source.Config = JsonSerializer.Serialize(secrets.Encrypt(source.Type, request.Config, existing));
        }

        if (request.Schedule is not null)
        {
            ValidateCron(request.Schedule);
            source.Schedule = request.Schedule.Trim();
        }

        if (request.TimeZone is not null)
        {
            source.TimeZone = NormalizeTimeZone(request.TimeZone);
        }

        // An omitted filter leaves the stored one alone; an empty object clears it, which is the
        // only way to say "stop filtering" when null already means "don't touch".
        if (request.Filter is not null)
        {
            source.Filter = SourceFilters.Serialize(SourceFilters.Normalize(request.Filter));
        }

        if (request.Visibility is not null && request.Visibility.Value != source.Visibility)
        {
            // Shared → Private: drop every subscription from playlists the owner doesn't own.
            if (request.Visibility.Value == SourceVisibility.Private)
            {
                var foreign = await db.PlaylistSources
                    .Where(ps => ps.SourceId == id
                        && !db.Playlists.Any(p => p.Id == ps.PlaylistId && p.OwnerId == ownerId))
                    .ToListAsync(ct);
                db.PlaylistSources.RemoveRange(foreign);
            }

            source.Visibility = request.Visibility.Value;
        }

        if (request.PlaylistIds is not null)
        {
            await EnsurePlaylistsOwnedAsync(ownerId, request.PlaylistIds, ct);
            // Replace only attachments to the OWNER's own playlists — never touch other users'
            // cross-user subscriptions to this (shared) source.
            var existingOwned = await db.PlaylistSources
                .Where(ps => ps.SourceId == id
                    && db.Playlists.Any(p => p.Id == ps.PlaylistId && p.OwnerId == ownerId))
                .ToListAsync(ct);
            db.PlaylistSources.RemoveRange(existingOwned);
            foreach (var playlistId in request.PlaylistIds)
            {
                db.PlaylistSources.Add(new PlaylistSource { SourceId = id, PlaylistId = playlistId });
            }
        }

        if (request.Status is not null)
        {
            // Resuming clears the failure streak. Whatever the owner just changed is their
            // attempt at a fix, and it deserves a fresh count rather than tripping immediately.
            if (request.Status.Value == SourceStatus.Active && source.Status != SourceStatus.Active)
            {
                source.ConsecutiveFailures = 0;
            }

            source.Status = request.Status.Value;
        }

        await db.SaveChangesAsync(ct);

        // The recurring job follows the status, not the cron. A stopped source keeps its schedule
        // so resuming restores the owner's cadence instead of guessing a default.
        if (source.Status == SourceStatus.Active)
        {
            scheduler.Schedule(source.Id, source.Schedule, source.TimeZone);
        }
        else
        {
            scheduler.Unschedule(source.Id);
        }

        return ToResponse(source, await PlaylistIdsAsync(id, ct));
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        scheduler.Unschedule(source.Id);

        var attachments = await db.PlaylistSources.Where(ps => ps.SourceId == id).ToListAsync(ct);
        db.PlaylistSources.RemoveRange(attachments);
        db.Sources.Remove(source); // soft delete
        await db.SaveChangesAsync(ct);
    }

    public async Task RunNowAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        await quotas.EnsureCanRunAsync(ownerId, ct);
        scheduler.TriggerNow(source.Id);
    }

    public async Task<IReadOnlyList<SourceRunResponse>> ListRunsAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        await FindOwnedAsync(ownerId, id, ct); // ownership check

        return await db.SourceRuns
            .Where(r => r.SourceId == id)
            .OrderByDescending(r => r.CreationTime)
            .Take(50)
            .Select(r => new SourceRunResponse(
                r.Id, r.CreationTime, r.FinishedAt, r.Status, r.ItemsFound, r.ItemsAdded, r.Error,
                r.FoundCount, r.AddedCount, r.SkippedCount))
            .ToListAsync(ct);
    }

    /// <summary>How far back the health figures look. Longer than that is history, not health.</summary>
    public const int HealthWindowDays = 30;

    public async Task<SourceHealthResponse> GetHealthAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        var since = DateTimeOffset.UtcNow.AddDays(-HealthWindowDays);

        var runs = await db.SourceRuns
            .Where(r => r.SourceId == id && r.CreationTime >= since && r.Status != SourceRunStatus.Running)
            .Select(r => new { r.Status, r.FoundCount, r.AddedCount })
            .ToListAsync(ct);

        var last = await db.SourceRuns
            .Where(r => r.SourceId == id)
            .OrderByDescending(r => r.CreationTime)
            .Select(r => new { r.Status, r.Error, r.CreationTime })
            .FirstOrDefaultAsync(ct);

        var succeeded = runs.Where(r => r.Status == SourceRunStatus.Succeeded).ToList();
        var failed = runs.Count - succeeded.Count;

        return new SourceHealthResponse(
            runs.Count,
            HealthWindowDays,
            succeeded.Count,
            failed,
            runs.Count == 0 ? null : (int)Math.Round(succeeded.Count * 100.0 / runs.Count),
            // Averaged over successful runs only: a failed run found nothing because it failed,
            // not because there was nothing to find, and counting it drags the figure down.
            succeeded.Count == 0 ? null : Math.Round(succeeded.Average(r => (double)r.FoundCount), 1),
            succeeded.Count == 0 ? null : Math.Round(succeeded.Average(r => (double)r.AddedCount), 1),
            succeeded.Count(r => r.FoundCount == 0),
            source.ConsecutiveFailures,
            last?.CreationTime,
            last?.Status,
            last?.Error);
    }

    public async Task<PreviewSourceResponse> PreviewAsync(Guid ownerId, PreviewSourceRequest request, CancellationToken ct = default)
    {
        var interpreter = ResolveInterpreter(request.Type);
        interpreter.ValidateConfig(request.Config);

        // Config comes straight from the request (plaintext secrets), no decryption needed.
        var fetch = await interpreter.FetchAsync(request.Config, state: null, ct);
        var links = fetch.Links
            .Take(PreviewLimit)
            .Select(l => new DiscoveredLinkDto(l.Url, l.Title))
            .ToList();

        return new PreviewSourceResponse(links.Count, links);
    }

    private ISourceInterpreter ResolveInterpreter(SourceType type) =>
        interpreters.FirstOrDefault(i => i.Type == type)
        ?? throw new ValidationException("type", $"Unsupported source type '{type}'.");

    private static void Validate(string name, string schedule, IReadOnlyDictionary<string, string> config, ISourceInterpreter interpreter)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "Name is required.");
        }

        ValidateCron(schedule);
        interpreter.ValidateConfig(config);
    }

    public const int MinIntervalMinutes = 5;

    /// <summary>
    /// Accepts an IANA zone this host can schedule against; an empty value means UTC. Rejected
    /// rather than silently ignored, so a typo doesn't quietly run a schedule in the wrong hours.
    /// </summary>
    private static string? NormalizeTimeZone(string? timeZone)
    {
        var trimmed = timeZone?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (!SourceTimeZone.IsValid(trimmed))
        {
            throw new ValidationException("timeZone", $"'{trimmed}' is not a time zone this server recognises.");
        }

        return trimmed;
    }

    private static void ValidateCron(string schedule)
    {
        if (!CronSchedule.IsValid(schedule, MinIntervalMinutes))
        {
            throw new ValidationException(
                "schedule",
                $"A valid 5-field cron expression is required, running no more than once every {MinIntervalMinutes} minutes.");
        }
    }

    private async Task EnsurePlaylistsOwnedAsync(Guid ownerId, Guid[]? playlistIds, CancellationToken ct)
    {
        if (playlistIds is null || playlistIds.Length == 0)
        {
            return;
        }

        var owned = await db.Playlists
            .Where(p => p.OwnerId == ownerId && playlistIds.Contains(p.Id))
            .CountAsync(ct);
        if (owned != playlistIds.Distinct().Count())
        {
            throw new ValidationException("playlistIds", "One or more playlists were not found.");
        }
    }

    private Task<Guid[]> PlaylistIdsAsync(Guid sourceId, CancellationToken ct) =>
        db.PlaylistSources.Where(ps => ps.SourceId == sourceId).Select(ps => ps.PlaylistId).ToArrayAsync(ct);

    private async Task<Source> FindOwnedAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        await db.Sources.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == ownerId, ct)
        ?? throw new NotFoundException("Source not found.");

    public async Task<IReadOnlyList<SharedSourceSummary>> ListSharedAsync(string? q, CancellationToken ct = default)
    {
        var query = db.Sources.Where(s => s.Visibility == SourceVisibility.Shared);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(needle));
        }

        return await (from s in query
                      join u in db.Users on s.OwnerId equals u.Id
                      orderby s.CreationTime descending
                      select new SharedSourceSummary(s.Id, s.Name, s.Type, u.UserName!, s.CreationTime))
            .Take(100)
            .ToListAsync(ct);
    }

    private SourceResponse ToResponse(Source source, Guid[] playlistIds, SourceRunStatus? lastRunStatus = null)
    {
        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(source.Config) ?? new();
        return new(
            source.Id, source.Name, source.Type, secrets.Redact(source.Type, stored),
            source.Schedule, source.Visibility, source.LastRunAt, source.CreationTime, playlistIds,
            lastRunStatus, source.Status, source.ConsecutiveFailures, source.TimeZone,
            SourceFilters.Deserialize(source.Filter));
    }
}
