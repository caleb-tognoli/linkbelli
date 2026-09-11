using System.Security.Cryptography;
using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Receives a push for a webhook source. The token in the URL is the whole credential, so this
/// resolves it and then hands the links to the ordinary source machinery.
/// </summary>
public interface IWebhookIngestService
{
    Task<WebhookPushResponse> PushAsync(string token, WebhookPushRequest request, CancellationToken ct = default);

    /// <summary>Mints the token a new webhook source is addressed by.</summary>
    static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <inheritdoc />
public sealed class WebhookIngestService(
    IAppDbContext db,
    ISourceRunner runner,
    SourceConfigSecrets secrets,
    PushedLinks pushed) : IWebhookIngestService
{
    /// <summary>Links accepted in one push. The per-run quota still applies on top.</summary>
    public const int MaxLinksPerPush = 100;

    public async Task<WebhookPushResponse> PushAsync(
        string token, WebhookPushRequest request, CancellationToken ct = default)
    {
        var links = (request.Links ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Url))
            .Take(MaxLinksPerPush)
            .Select(l => new DiscoveredLink(l.Url.Trim(), Trim(l.Title)))
            .ToList();

        if (links.Count == 0)
        {
            throw new ValidationException("links", "Send at least one link.");
        }

        var source = await ResolveAsync(token, ct);

        // Handed to the same runner every other source type uses, so a push gets the same filter,
        // the same dedup, the same run row and the same quota as a poll. A second ingestion path
        // is how two kinds of source quietly start behaving differently.
        pushed.Set(links);
        await runner.RunAsync(source.Id, ct);

        var run = await db.SourceRuns
            .AsNoTracking()
            .Where(r => r.SourceId == source.Id)
            .OrderByDescending(r => r.CreationTime)
            .Select(r => new { r.Status, r.FoundCount, r.AddedCount, r.SkippedCount, r.Error })
            .FirstOrDefaultAsync(ct);

        return new WebhookPushResponse(
            links.Count,
            run?.FoundCount ?? 0,
            run?.AddedCount ?? 0,
            run?.SkippedCount ?? 0,
            run?.Status ?? SourceRunStatus.Failed,
            run?.Error);
    }

    /// <summary>
    /// Finds the source a token addresses.
    /// </summary>
    /// <remarks>
    /// Tokens are stored encrypted like any other source secret, so they cannot be looked up with
    /// a WHERE clause. Webhook sources are few, and this runs once per push.
    /// </remarks>
    private async Task<Source> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new NotFoundException("No such webhook.");
        }

        var candidates = await db.Sources
            .Where(s => s.Type == SourceType.Webhook)
            .Select(s => new { s.Id, s.Config })
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.Config) ?? [];
            var config = secrets.Decrypt(SourceType.Webhook, stored);

            if (config.TryGetValue(WebhookSourceInterpreter.TokenKey, out var stored_token)
                && CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(stored_token),
                    System.Text.Encoding.UTF8.GetBytes(token)))
            {
                return await db.Sources.FirstAsync(s => s.Id == candidate.Id, ct);
            }
        }

        // The same answer as a token that never existed: a webhook URL is a secret, and a
        // different reply for "real but disabled" would confirm one exists.
        throw new NotFoundException("No such webhook.");
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
