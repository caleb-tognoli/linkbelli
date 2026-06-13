using System.Net;
using System.Net.Http;
using System.Text.Json;
using CodeHollow.FeedReader;
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
            .Select(i => new DiscoveredLink(
                i.Link!.Trim(),
                string.IsNullOrWhiteSpace(i.Title) ? null : i.Title!.Trim()))
            .ToList();
    }
}
