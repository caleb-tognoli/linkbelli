using System.Net.Http;
using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

public class LinkEnricher(
    IHttpClientFactory httpClientFactory,
    LinkMetadataExtractor extractor,
    IAppDbContext db,
    IHostThrottle throttle,
    ILogger<LinkEnricher> logger) : ILinkEnricher
{
    public async Task EnrichAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await db.Links.Include(l => l.Host).FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return;
        }

        try
        {
            // Serialize requests per host so parallel Hangfire workers don't dogpile the same
            // origin — the primary cause of the 429 wave we hit on the TMDB seed.
            await throttle.WaitAsync(link.Host!.Hostname, cancellationToken);

            var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
            using var response = await client.GetAsync(link.CanonicalUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx is permanent — stamp so we stop retrying; 5xx and 429 are transient — throw so
                // the job runner retries with backoff instead of stamping a dead-end failure.
                var status = (int)response.StatusCode;
                if (status is >= 400 and < 500 and not 429)
                {
                    StampFailure(link, $"HTTP {status}");
                    await db.SaveChangesAsync(cancellationToken);
                    return;
                }

                throw new HttpRequestException($"Enrichment fetch returned HTTP {status} (transient).");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                StampFailure(link, $"Non-HTML content ({mediaType})");
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var metadata = extractor.Extract(html);

            // Only fill in the title when it's still empty — a source-provided title (e.g. TMDB
            // via urlTemplate + titlePath) is more trustworthy than the target page's OG tag,
            // which can be localized (e.g. Finnish) and would otherwise clobber it.
            if (string.IsNullOrWhiteSpace(link.Title))
            {
                link.Title = metadata.Title;
            }
            link.Description = metadata.Description;
            link.ThumbnailUrl = metadata.ImageUrl;
            link.SiteName = metadata.SiteName;
            link.Metadata = metadata.Raw.Count > 0 ? JsonSerializer.Serialize(metadata.Raw) : null;
            if (metadata.Nsfw)
            {
                link.Nsfw = true; // automatic; never cleared
            }

            link.EnrichedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Transient (network/timeout/SSRF-block/oversize): rethrow so the job runner
            // retries with backoff. Permanent failures are stamped above and return normally.
            logger.LogWarning(ex, "Enrichment failed for link {LinkId} ({Url})", linkId, link.CanonicalUrl);
            throw;
        }
    }

    private static void StampFailure(Core.Entities.Link link, string reason)
    {
        link.Metadata = JsonSerializer.Serialize(new Dictionary<string, string> { ["enrichmentError"] = reason });
        link.EnrichedAt = DateTimeOffset.UtcNow;
    }
}
