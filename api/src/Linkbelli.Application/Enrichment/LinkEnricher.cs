using System.Net;
using System.Net.Http;
using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Http;
using System.Diagnostics;
using Linkbelli.Application.Observability;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

public class LinkEnricher(
    IHttpClientFactory httpClientFactory,
    LinkMetadataExtractor extractor,
    ArticleExtractor articles,
    IAppDbContext db,
    IHostThrottle throttle,
    AppMetrics metrics,
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

        // Timed from here, including the per-host wait: that wait is part of how long a link
        // actually takes to appear, whatever the reason for it.
        var started = Stopwatch.GetTimestamp();

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
                    StampFailure(link, DescribeStatus(status), ClassifyStatus(status));
                    await db.SaveChangesAsync(cancellationToken);
                    Record(link, started);
                    return;
                }

                throw new HttpRequestException($"Enrichment fetch returned HTTP {status} (transient).");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                StampFailure(link, $"That address is not a web page ({mediaType}).");
                await db.SaveChangesAsync(cancellationToken);
                Record(link, started);
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

            // The page is already here and already parsed once; reading the article out of it now
            // is the only chance we get, since the copy on the web is the part that rots.
            var article = articles.Extract(html);
            link.Content = article.Text;
            link.WordCount = article.Text is null ? null : article.WordCount;
            link.ContentTruncated = article.Truncated;

            link.Kind = ContentClassifier.Classify(
                link.CanonicalUrl,
                link.Host.Hostname,
                metadata.Raw.GetValueOrDefault("og:type"),
                mediaType,
                article.Text is null ? null : article.WordCount);
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

            StampSuccess(link);
            await db.SaveChangesAsync(cancellationToken);

            Record(link, started);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Transient (network/timeout/SSRF-block/oversize): rethrow so the job runner
            // retries with backoff. Permanent failures are stamped above and return normally.
            Record(link, started, "errored");
            logger.LogWarning(ex, "Enrichment failed for link {LinkId} ({Url})", linkId, link.CanonicalUrl);
            throw;
        }
    }

    /// <summary>
    /// Counts one attempt. The outcome comes from the link's own status unless the fetch threw,
    /// which is a different thing from a page that answered with an error.
    /// </summary>
    private void Record(Link link, long started, string? outcome = null) =>
        metrics.Enrichment(
            outcome ?? link.EnrichmentStatus.ToString().ToLowerInvariant(),
            link.Host?.Hostname ?? "unknown",
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);

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
                // 401/404 from oEmbed means private, deleted or unlisted — the video is gone.
                StampFailure(link, DescribeStatus(status),
                    status is 401 or 404 ? EnrichmentStatus.Broken : EnrichmentStatus.Failed);
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

        // oEmbed returns no page to read, so the host is all there is to go on — which for this
        // path is enough, since only video hosts come down it.
        link.Kind = ContentClassifier.Classify(link.CanonicalUrl, link.Host!.Hostname);

        StampSuccess(link);
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

    /// <summary>410 Gone and 404 mean the page is not there; everything else is a fetch problem.</summary>
    private static EnrichmentStatus ClassifyStatus(int status) =>
        status is 404 or 410 ? EnrichmentStatus.Broken : EnrichmentStatus.Failed;

    /// <summary>Says what happened in words someone reading a playlist would understand.</summary>
    private static string DescribeStatus(int status) => status switch
    {
        404 => "The page could not be found (404).",
        410 => "The page has been removed (410).",
        401 or 403 => $"The site refused the request ({status}).",
        _ => $"The site returned HTTP {status}.",
    };

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// A fetch we are not going to retry right now. This used to stamp EnrichedAt and hide the
    /// reason inside the OpenGraph metadata bag, which made a dead link indistinguishable from a
    /// successfully enriched one — it simply rendered as a bare URL forever.
    /// </summary>
    private static void StampFailure(Link link, string reason, EnrichmentStatus status = EnrichmentStatus.Failed)
    {
        var now = DateTimeOffset.UtcNow;

        link.EnrichmentStatus = status;
        link.EnrichmentError = reason;
        link.LastCheckedAt = now;
        link.FailureCount++;

        // Still stamped, because reads gate on it: the item should appear, labelled, rather than
        // sit invisible in a playlist its owner can see the count of.
        link.EnrichedAt ??= now;
    }

    private static void StampSuccess(Link link)
    {
        var now = DateTimeOffset.UtcNow;

        link.EnrichmentStatus = EnrichmentStatus.Succeeded;
        link.EnrichmentError = null;
        link.LastCheckedAt = now;
        link.FailureCount = 0;
        link.EnrichedAt ??= now;
    }
}
