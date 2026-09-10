using System.Net;
using System.Net.Http;
using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
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
    private static readonly HashSet<string> YouTubeHosts = new(StringComparer.Ordinal)
    {
        "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be",
    };

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

            // YouTube serves an anti-bot interstitial to the generic fetch; use the public oEmbed
            // endpoint instead — it returns title/author/thumbnail as JSON, no auth, no bot check.
            if (YouTubeHosts.Contains(link.Host.Hostname))
            {
                await EnrichViaYouTubeOEmbedAsync(link, client, cancellationToken);
                return;
            }

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

            // og:site_name only — LinkMetadata.SiteName falls back to the page <title>, which
            // names one article rather than the site it lives on.
            ApplyHostBranding(
                link.Host,
                metadata.Raw.GetValueOrDefault("og:site_name"),
                FaviconResolver.Resolve(link.CanonicalUrl, metadata.FaviconHref));

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

    private async Task EnrichViaYouTubeOEmbedAsync(Link link, HttpClient client, CancellationToken ct)
    {
        var oembedUrl = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(link.CanonicalUrl);
        using var response = await client.GetAsync(oembedUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            // 401/404 = private/deleted/unlisted — permanent. Other 4xx (except 429) also permanent.
            if (status is >= 400 and < 500 and not 429)
            {
                StampFailure(link, $"YouTube oEmbed HTTP {status}");
                await db.SaveChangesAsync(ct);
                return;
            }

            throw new HttpRequestException($"YouTube oEmbed returned HTTP {status} (transient).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        var title = ReadString(root, "title");
        var author = ReadString(root, "author_name");
        var thumbnail = ReadString(root, "thumbnail_url");
        var providerName = ReadString(root, "provider_name") ?? "YouTube";

        if (string.IsNullOrWhiteSpace(link.Title))
        {
            link.Title = title;
        }
        link.ThumbnailUrl = thumbnail;
        link.SiteName = providerName;

        var raw = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(title)) raw["title"] = title!;
        if (!string.IsNullOrWhiteSpace(author)) raw["author"] = author!;
        if (!string.IsNullOrWhiteSpace(thumbnail)) raw["thumbnail"] = thumbnail!;
        raw["provider_name"] = providerName;
        link.Metadata = JsonSerializer.Serialize(raw);

        ApplyHostBranding(link.Host!, providerName, FaviconResolver.Resolve(link.CanonicalUrl, null));

        link.EnrichedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fills in the Host row's display name and favicon the first time a page on that site tells
    /// us what they are. Both are per-site, so the first enriched link wins and later ones leave
    /// them alone — re-deriving them from every page would flap on sites with per-section icons.
    /// </summary>
    private static void ApplyHostBranding(Core.Entities.Host host, string? siteName, string? faviconUrl)
    {
        if (string.IsNullOrWhiteSpace(host.DisplayName) && !string.IsNullOrWhiteSpace(siteName))
        {
            host.DisplayName = siteName.Trim();
        }

        if (string.IsNullOrWhiteSpace(host.Favicon) && !string.IsNullOrWhiteSpace(faviconUrl))
        {
            host.Favicon = faviconUrl;
        }
    }

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static void StampFailure(Link link, string reason)
    {
        link.Metadata = JsonSerializer.Serialize(new Dictionary<string, string> { ["enrichmentError"] = reason });
        link.EnrichedAt = DateTimeOffset.UtcNow;
    }
}
