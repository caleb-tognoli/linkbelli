using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Automation;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Automation;

/// <summary>Managing the rules themselves. Running them is <see cref="IAutomationRunner"/>.</summary>
public interface IAutomationRuleService
{
    Task<IReadOnlyList<AutomationRuleResponse>> ListAsync(Guid ownerId, CancellationToken ct = default);

    Task<AutomationRuleResponse> CreateAsync(
        Guid ownerId, CreateAutomationRuleRequest request, CancellationToken ct = default);

    Task<AutomationRuleResponse> UpdateAsync(
        Guid ownerId, Guid id, UpdateAutomationRuleRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// What this rule would match among items already saved. A rule only ever acts on what
    /// arrives next, so without this there is no way to find out whether it works except by
    /// waiting to see what it does.
    /// </summary>
    Task<AutomationPreviewResponse> PreviewAsync(
        Guid ownerId, CreateAutomationRuleRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AutomationRuleService(IAppDbContext db) : IAutomationRuleService
{
    /// <summary>Items shown by a preview. Enough to recognise a mistake, not a second search.</summary>
    private const int PreviewSize = 10;

    /// <summary>
    /// Rules per person. A generous ceiling that still bounds what every arriving item has to be
    /// tested against.
    /// </summary>
    public const int MaxRules = 50;

    public async Task<IReadOnlyList<AutomationRuleResponse>> ListAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.AutomationRules
            .AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .OrderBy(r => r.Position)
            .ThenBy(r => r.CreationTime)
            .Select(r => ToResponse(r))
            .ToListAsync(ct);

    public async Task<AutomationRuleResponse> CreateAsync(
        Guid ownerId, CreateAutomationRuleRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(ownerId, request.Name, request.TitlePattern, request.UrlPattern,
            request.PlaylistId, request.MoveToPlaylistId, request.CopyToPlaylistId,
            request.MoveToPlaylistId is not null && request.CopyToPlaylistId is not null, ct);

        if (await db.AutomationRules.CountAsync(r => r.OwnerId == ownerId, ct) >= MaxRules)
        {
            throw new ValidationException("rules", $"You can have at most {MaxRules} rules.");
        }

        var rule = new AutomationRule
        {
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Enabled = request.Enabled,
            Position = request.Position ?? await NextPositionAsync(ownerId, ct),
            PlaylistId = request.PlaylistId,
            Host = Normalize(request.Host),
            TitlePattern = request.TitlePattern?.Trim(),
            UrlPattern = request.UrlPattern?.Trim(),
            Kind = request.Kind,
            MaxMinutes = request.MaxMinutes,
            MinMinutes = request.MinMinutes,
            Broken = request.Broken,
            SourceId = request.SourceId,
            AddTags = TagNormalizer.Normalize(request.AddTags ?? []).ToArray(),
            MoveToPlaylistId = request.MoveToPlaylistId,
            CopyToPlaylistId = request.CopyToPlaylistId,
            MarkWatched = request.MarkWatched,
            Trash = request.Trash,
            SetScore = request.SetScore,
            Archive = request.Archive,
            StopOnMatch = request.StopOnMatch,
        };

        db.AutomationRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return ToResponse(rule);
    }

    public async Task<AutomationRuleResponse> UpdateAsync(
        Guid ownerId, Guid id, UpdateAutomationRuleRequest request, CancellationToken ct = default)
    {
        var rule = await FindOwnedAsync(ownerId, id, ct);
        var clear = new HashSet<string>(request.Clear ?? [], StringComparer.OrdinalIgnoreCase);

        var move = clear.Contains(nameof(rule.MoveToPlaylistId)) ? null : request.MoveToPlaylistId ?? rule.MoveToPlaylistId;
        var copy = clear.Contains(nameof(rule.CopyToPlaylistId)) ? null : request.CopyToPlaylistId ?? rule.CopyToPlaylistId;

        await ValidateAsync(ownerId, request.Name ?? rule.Name, request.TitlePattern, request.UrlPattern,
            request.PlaylistId, move, copy, move is not null && copy is not null, ct);

        if (request.Name is not null) rule.Name = request.Name.Trim();
        if (request.Enabled is { } enabled) rule.Enabled = enabled;
        if (request.Position is { } position) rule.Position = position;
        if (request.Kind is { } kind) rule.Kind = kind;
        if (request.MaxMinutes is { } most) rule.MaxMinutes = most;
        if (request.MinMinutes is { } least) rule.MinMinutes = least;
        if (request.Broken is { } broken) rule.Broken = broken;
        if (request.SourceId is { } source) rule.SourceId = source;
        if (request.MarkWatched is { } watched) rule.MarkWatched = watched;
        if (request.Trash is { } trash) rule.Trash = trash;
        if (request.SetScore is { } score) rule.SetScore = score;
        if (request.Archive is { } archive) rule.Archive = archive;
        if (request.StopOnMatch is { } stop) rule.StopOnMatch = stop;
        if (request.AddTags is not null) rule.AddTags = TagNormalizer.Normalize(request.AddTags).ToArray();
        if (request.Host is not null) rule.Host = Normalize(request.Host);
        if (request.TitlePattern is not null) rule.TitlePattern = request.TitlePattern.Trim();
        if (request.UrlPattern is not null) rule.UrlPattern = request.UrlPattern.Trim();
        if (request.PlaylistId is { } scope) rule.PlaylistId = scope;

        rule.MoveToPlaylistId = move;
        rule.CopyToPlaylistId = copy;

        // Null means "leave it alone" everywhere else on this request, so clearing a condition
        // has to be said out loud.
        foreach (var field in clear)
        {
            switch (field.ToLowerInvariant())
            {
                case "playlistid": rule.PlaylistId = null; break;
                case "host": rule.Host = null; break;
                case "titlepattern": rule.TitlePattern = null; break;
                case "urlpattern": rule.UrlPattern = null; break;
                case "kind": rule.Kind = null; break;
                case "maxminutes": rule.MaxMinutes = null; break;
                case "minminutes": rule.MinMinutes = null; break;
                case "broken": rule.Broken = null; break;
                case "sourceid": rule.SourceId = null; break;
                case "setscore": rule.SetScore = null; break;
                case "addtags": rule.AddTags = []; break;
            }
        }

        await db.SaveChangesAsync(ct);

        return ToResponse(rule);
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var rule = await FindOwnedAsync(ownerId, id, ct);

        db.AutomationRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AutomationPreviewResponse> PreviewAsync(
        Guid ownerId, CreateAutomationRuleRequest request, CancellationToken ct = default)
    {
        var compiled = new CompiledRule(new AutomationRule
        {
            OwnerId = ownerId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Preview" : request.Name,
            PlaylistId = request.PlaylistId,
            Host = Normalize(request.Host),
            TitlePattern = Pattern(request.TitlePattern, "titlePattern"),
            UrlPattern = Pattern(request.UrlPattern, "urlPattern"),
            Kind = request.Kind,
            MaxMinutes = request.MaxMinutes,
            MinMinutes = request.MinMinutes,
            Broken = request.Broken,
            SourceId = request.SourceId,
        });

        // The cheap conditions narrow in the database; the patterns are applied here, because
        // they are regular expressions and Postgres would need a different dialect of them.
        var query = db.PlaylistItems
            .AsNoTracking()
            .Include(i => i.Link).ThenInclude(l => l!.Host)
            .Include(i => i.Playlist)
            .Where(i => i.Playlist!.OwnerId == ownerId && i.Link!.EnrichedAt != null);

        if (request.PlaylistId is { } playlistId) query = query.Where(i => i.PlaylistId == playlistId);
        if (Normalize(request.Host) is { } host) query = query.Where(i => i.Link!.Host!.Hostname == host);
        if (request.Kind is { } kind) query = query.Where(i => i.Link!.Kind == kind);
        if (request.SourceId is { } source) query = query.Where(i => i.SourceId == source);
        if (request.Broken is { } broken)
        {
            query = broken
                ? query.Where(i => i.Link!.EnrichmentStatus == EnrichmentStatus.Broken)
                : query.Where(i => i.Link!.EnrichmentStatus != EnrichmentStatus.Broken);
        }

        // Length narrows here too rather than only in the matcher: a preview over five hundred
        // rows is the one place where pulling back what cannot match costs something visible.
        if (request.MaxMinutes is { } most)
        {
            var words = ReadingTime.WordsWithin(most);
            query = query.Where(i => i.Link!.WordCount != null && i.Link.WordCount <= words);
        }

        if (request.MinMinutes is { } least)
        {
            // One minute means "at least a minute", so the floor is what a one-minute read
            // rounds up from rather than a whole minute of words.
            var words = ReadingTime.WordsWithin(least - 1);
            query = query.Where(i => i.Link!.WordCount != null && i.Link.WordCount > words);
        }

        var candidates = await query
            .OrderByDescending(i => i.CreationTime)
            .Take(500)
            .ToListAsync(ct);

        var matched = candidates
            .Where(i => compiled.Matches(new RuleCandidate(
                i.PlaylistId,
                i.Link!.CanonicalUrl,
                i.Link.Host?.Hostname ?? string.Empty,
                i.Metadata?.GetValueOrDefault("title") ?? i.Link.Title,
                i.Link.Kind,
                i.Link.WordCount,
                i.Link.EnrichmentStatus == EnrichmentStatus.Broken,
                i.SourceId)))
            .ToList();

        return new AutomationPreviewResponse(
            matched.Count,
            [.. matched.Take(PreviewSize).Select(i => new AutomationPreviewItem(
                i.Id, i.Playlist!.Name, i.Link!.CanonicalUrl,
                i.Metadata?.GetValueOrDefault("title") ?? i.Link.Title))]);
    }

    private async Task ValidateAsync(
        Guid ownerId, string name, string? titlePattern, string? urlPattern,
        Guid? scope, Guid? moveTo, Guid? copyTo, bool bothDestinations, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "A name is required.");
        }

        Pattern(titlePattern, "titlePattern");
        Pattern(urlPattern, "urlPattern");

        if (bothDestinations)
        {
            throw new ValidationException(
                "moveToPlaylistId", "A rule can move an item or copy it, not both.");
        }

        foreach (var (playlistId, field) in new[]
                 {
                     (scope, "playlistId"), (moveTo, "moveToPlaylistId"), (copyTo, "copyToPlaylistId"),
                 })
        {
            if (playlistId is { } id
                && !await db.Playlists.AnyAsync(p => p.Id == id && p.OwnerId == ownerId, ct))
            {
                throw new ValidationException(field, "That playlist isn't yours.");
            }
        }
    }

    /// <summary>Checks a pattern where the person who typed it can be told, and returns it tidied.</summary>
    private static string? Pattern(string? pattern, string field)
    {
        var trimmed = pattern?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        try
        {
            CompiledRule.Build(trimmed);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(field, ex.Message);
        }

        return trimmed;
    }

    private async Task<AutomationRule> FindOwnedAsync(Guid ownerId, Guid id, CancellationToken ct) =>
        await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == id && r.OwnerId == ownerId, ct)
        ?? throw new NotFoundException("Rule not found.");

    private async Task<int> NextPositionAsync(Guid ownerId, CancellationToken ct) =>
        (await db.AutomationRules.Where(r => r.OwnerId == ownerId).MaxAsync(r => (int?)r.Position, ct) ?? 0) + 1;

    private static string? Normalize(string? host)
    {
        var trimmed = host?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static AutomationRuleResponse ToResponse(AutomationRule r) => new(
        r.Id, r.Name, r.Enabled, r.Position, r.PlaylistId, r.Host, r.TitlePattern, r.UrlPattern,
        r.Kind, r.MaxMinutes, r.MinMinutes, r.Broken, r.SourceId,
        r.AddTags, r.MoveToPlaylistId, r.CopyToPlaylistId, r.MarkWatched, r.Trash,
        r.SetScore, r.Archive, r.StopOnMatch, r.MatchCount, r.LastMatchedAt, r.CreationTime);
}
