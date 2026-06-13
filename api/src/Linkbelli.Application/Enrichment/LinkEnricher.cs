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
    ILogger<LinkEnricher> logger) : ILinkEnricher
{
    public async Task EnrichAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await db.Links.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
        {
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
            using var response = await client.GetAsync(link.CanonicalUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // 4xx is permanent — stamp so we stop retrying; 5xx is transient — leave for a later sweep.
                if ((int)response.StatusCode is >= 400 and < 500)
                {
                    StampFailure(link, $"HTTP {(int)response.StatusCode}");
                    await db.SaveChangesAsync(cancellationToken);
                }

                return;
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

            link.Title = metadata.Title;
            link.Description = metadata.Description;
            link.ThumbnailUrl = metadata.ImageUrl;
            link.SiteName = metadata.SiteName;
            link.Metadata = metadata.Raw.Count > 0 ? JsonSerializer.Serialize(metadata.Raw) : null;
            link.EnrichedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Transient (network/timeout/SSRF-block/oversize): leave EnrichedAt null for a future retry.
            logger.LogWarning(ex, "Enrichment failed for link {LinkId} ({Url})", linkId, link.CanonicalUrl);
        }
    }

    private static void StampFailure(Core.Entities.Link link, string reason)
    {
        link.Metadata = JsonSerializer.Serialize(new Dictionary<string, string> { ["enrichmentError"] = reason });
        link.EnrichedAt = DateTimeOffset.UtcNow;
    }
}
