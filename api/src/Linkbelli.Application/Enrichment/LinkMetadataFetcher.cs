using System.Net.Http;
using Linkbelli.Application.Http;

namespace Linkbelli.Application.Enrichment;

/// <summary>
/// Best-effort fetch + extract of page metadata through the SSRF-protected client, for the
/// link-preview flow (paste → preview → confirm). Returns null on any failure (non-success,
/// non-HTML, blocked, timeout) — preview never throws on a bad page, it just shows less.
/// </summary>
public class LinkMetadataFetcher(IHttpClientFactory httpClientFactory, LinkMetadataExtractor extractor)
{
    public async Task<LinkMetadata?> TryFetchAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            return extractor.Extract(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
