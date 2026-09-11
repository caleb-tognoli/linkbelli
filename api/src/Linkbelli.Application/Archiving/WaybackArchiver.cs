using System.Net;
using System.Text.Json;
using Linkbelli.Application.Http;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Archiving;

/// <summary>The outcome of trying to get a page archived.</summary>
/// <param name="Url">The snapshot's address, or null when there isn't one.</param>
/// <param name="Retry">
/// Whether trying again later could work. A rate limit is worth retrying; a page the archive
/// refuses outright is not.
/// </param>
public record ArchiveResult(string? Url, bool Retry);

/// <summary>
/// Keeps a public copy of a page somewhere that outlives the site it came from.
/// </summary>
public interface IArchiver
{
    /// <summary>Finds an existing snapshot, or asks for one to be made.</summary>
    Task<ArchiveResult> ArchiveAsync(string url, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// The availability API is asked first, because most pages worth saving have already been
/// archived by someone, and asking is far cheaper than submitting — the save endpoint fetches
/// and stores the whole page, is heavily rate-limited, and is the neighbourly thing to use
/// sparingly.
/// </remarks>
public sealed class WaybackArchiver(
    IHttpClientFactory httpClientFactory,
    ILogger<WaybackArchiver> logger) : IArchiver
{
    private const string AvailabilityEndpoint = "https://archive.org/wayback/available?url=";
    private const string SaveEndpoint = "https://web.archive.org/save/";

    public async Task<ArchiveResult> ArchiveAsync(string url, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);

        var existing = await FindExistingAsync(client, url, cancellationToken);
        if (existing is not null)
        {
            return new ArchiveResult(existing, Retry: false);
        }

        return await SaveAsync(client, url, cancellationToken);
    }

    private async Task<string?> FindExistingAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(AvailabilityEndpoint + Uri.EscapeDataString(url), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            return ReadSnapshot(document.RootElement);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A lookup that fails is not a failure to archive — the save below still gets its go.
            logger.LogDebug(ex, "Archive lookup failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Pulls the snapshot URL out of an availability response, which nests it three deep and
    /// reports "no snapshot" as an empty object rather than as an absent one.
    /// </summary>
    public static string? ReadSnapshot(JsonElement root)
    {
        if (!root.TryGetProperty("archived_snapshots", out var snapshots)
            || !snapshots.TryGetProperty("closest", out var closest)
            || closest.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var available = !closest.TryGetProperty("available", out var flag) || flag.ValueKind != JsonValueKind.False;
        if (!available || !closest.TryGetProperty("url", out var snapshotUrl))
        {
            return null;
        }

        var value = snapshotUrl.GetString();

        // Snapshot URLs come back scheme-relative or over plain http often enough to matter, and
        // this ends up in an href on a page served over https.
        return string.IsNullOrWhiteSpace(value) ? null : Https(value);
    }

    /// <summary>Normalizes a snapshot address to https.</summary>
    public static string Https(string url) =>
        url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url
        : url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..]
        : url;

    private async Task<ArchiveResult> SaveAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SaveEndpoint + url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError)
            {
                // Theirs, not ours, and it will pass.
                logger.LogInformation("Archive save deferred for {Url}: HTTP {Status}.", url, (int)response.StatusCode);
                return new ArchiveResult(null, Retry: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Refused: robots.txt, a paywall, a host that blocks the crawler. Asking again
                // tomorrow would get the same answer.
                logger.LogInformation("Archive refused {Url}: HTTP {Status}.", url, (int)response.StatusCode);
                return new ArchiveResult(null, Retry: false);
            }

            // The save endpoint redirects to the snapshot it just made; the client follows it, so
            // the final address is the snapshot.
            var snapshot = response.RequestMessage?.RequestUri?.ToString();

            return snapshot is not null && snapshot.Contains("/web/", StringComparison.Ordinal)
                ? new ArchiveResult(Https(snapshot), Retry: false)
                : new ArchiveResult(null, Retry: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A timeout is the common case here: the save endpoint fetches the whole page first.
            logger.LogInformation(ex, "Archive save failed for {Url}", url);
            return new ArchiveResult(null, Retry: true);
        }
    }
}
