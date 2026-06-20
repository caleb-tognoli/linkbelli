using System.Net;
using System.Net.Http;
using System.Text.Json;
using CodeHollow.FeedReader;
using CodeHollow.FeedReader.Feeds;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;

namespace Linkbelli.Application.Sources;

/// <summary>
/// RSS/Atom source: fetches a feed (through the SSRF-protected client) and yields each
/// entry's link. Config: { "feedUrl": "https://…" }. Uses conditional GET (ETag /
/// Last-Modified, persisted in <see cref="Source.State"/>) to skip unchanged feeds.
/// </summary>
public sealed class RssSourceInterpreter(IHttpClientFactory httpClientFactory) : ISourceInterpreter
{
    public const string FeedUrlKey = "feedUrl";
    private const int MaxItemsPerRun = 100;

    public SourceType Type => SourceType.Rss;

    public void ValidateConfig(IReadOnlyDictionary<string, string> config)
    {
        if (!config.TryGetValue(FeedUrlKey, out var url) || !UrlCanonicalizer.TryCanonicalize(url, out _))
        {
            throw new ValidationException($"config.{FeedUrlKey}", "A valid http(s) feed URL is required.");
        }
    }

    /// <summary>Conditional-GET validators persisted between runs.</summary>
    private record FeedState(string? ETag, string? LastModified);

    public async Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
    {
        var feedUrl = config[FeedUrlKey];
        var prior = state is null ? null : JsonSerializer.Deserialize<FeedState>(state);

        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
        using var request = new HttpRequestMessage(HttpMethod.Get, feedUrl);
        if (!string.IsNullOrEmpty(prior?.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", prior.ETag);
        }

        if (!string.IsNullOrEmpty(prior?.LastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", prior.LastModified);
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new SourceFetchResult([], state, NotModified: true);
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);

        var newState = JsonSerializer.Serialize(new FeedState(
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified?.ToString("R")));

        return new SourceFetchResult(ParseFeed(xml), newState);
    }

    /// <summary>Pure parse of feed XML into discovered links — unit-testable from fixtures.</summary>
    public static IReadOnlyList<DiscoveredLink> ParseFeed(string xml)
    {
        var feed = FeedReader.ReadFromString(xml);
        return feed.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Link))
            .Take(MaxItemsPerRun)
            .Select(i =>
            {
                var meta = new Dictionary<string, string>();
                if (!string.IsNullOrWhiteSpace(i.Title))  meta["title"]  = i.Title!.Trim();
                if (!string.IsNullOrWhiteSpace(i.Author)) meta["author"] = i.Author!.Trim();
                var thumb = ExtractThumbnail(i);
                if (thumb is not null) meta["thumbnail"] = thumb;
                return new DiscoveredLink(i.Link!.Trim(), null, meta.Count > 0 ? meta : null);
            })
            .ToList();
    }

    private static string? ExtractThumbnail(FeedItem item)
    {
        // media:thumbnail / media:content (Media RSS)
        if (item.SpecificItem is MediaRssFeedItem mediaItem)
        {
            var fromThumb = mediaItem.Media?
                .SelectMany(m => m.Thumbnails)
                .Select(t => t.Url)
                .FirstOrDefault(u => !string.IsNullOrEmpty(u));
            if (fromThumb is not null) return fromThumb;

            var fromMedia = mediaItem.MediaGroups?
                .SelectMany(g => g.Media)
                .SelectMany(m => m.Thumbnails)
                .Select(t => t.Url)
                .FirstOrDefault(u => !string.IsNullOrEmpty(u));
            if (fromMedia is not null) return fromMedia;
        }

        // <enclosure> with an image MIME type (RSS 2.0)
        var enclosure = item.SpecificItem switch
        {
            Rss20FeedItem r => r.Enclosure,
            Rss092FeedItem r2 => r2.Enclosure,
            _ => null
        };
        if (enclosure is { Url: { Length: > 0 } url } && enclosure.MediaType?.StartsWith("image/") == true)
            return url;

        return null;
    }
}
