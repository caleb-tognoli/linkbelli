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
    IUserQuotaService quotas) : ISourceService
{
    private const int PreviewLimit = 10;

    public async Task<IReadOnlyList<SourceResponse>> ListAsync(Guid ownerId, CancellationToken ct = default)
    {
        var sources = await db.Sources
            .AsNoTracking()
            .Where(s => s.OwnerId == ownerId)
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync(ct);

        var result = new List<SourceResponse>(sources.Count);
        foreach (var source in sources)
        {
            result.Add(ToResponse(source, await PlaylistIdsAsync(source.Id, ct)));
        }

        return result;
    }

    public async Task<SourceResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        return ToResponse(source, await PlaylistIdsAsync(id, ct));
    }

    public async Task<SourceResponse> CreateAsync(Guid ownerId, CreateSourceRequest request, CancellationToken ct = default)
    {
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
            Nsfw = request.Nsfw ?? false,
            Enabled = true,
        };
        db.Sources.Add(source);

        foreach (var playlistId in request.PlaylistIds ?? [])
        {
            db.PlaylistSources.Add(new PlaylistSource { SourceId = source.Id, PlaylistId = playlistId });
        }

        await db.SaveChangesAsync(ct);
        scheduler.Schedule(source.Id, source.Schedule);

        return ToResponse(source, (request.PlaylistIds ?? []).ToArray());
    }

    public async Task<SourceResponse> UpdateAsync(Guid ownerId, Guid id, UpdateSourceRequest request, CancellationToken ct = default)
    {
        var source = await FindOwnedAsync(ownerId, id, ct);
        var interpreter = ResolveInterpreter(source.Type);

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

        if (request.Enabled is not null)
        {
            source.Enabled = request.Enabled.Value;
        }

        if (request.Nsfw is not null)
        {
            source.Nsfw = request.Nsfw.Value;
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

        await db.SaveChangesAsync(ct);

        if (source.Enabled)
        {
            scheduler.Schedule(source.Id, source.Schedule);
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
                r.Id, r.CreationTime, r.FinishedAt, r.Status, r.ItemsFound, r.ItemsAdded, r.Error))
            .ToListAsync(ct);
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

    private SourceResponse ToResponse(Source source, Guid[] playlistIds)
    {
        var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(source.Config) ?? new();
        return new(
            source.Id, source.Name, source.Type, secrets.Redact(source.Type, stored),
            source.Schedule, source.Enabled, source.Visibility, source.Nsfw, source.LastRunAt, source.CreationTime, playlistIds);
    }
}
