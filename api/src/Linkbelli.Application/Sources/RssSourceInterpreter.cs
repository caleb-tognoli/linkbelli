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
/// entry's link. Config: { "feedUrl": "https://…" }.
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

    public async Task<IReadOnlyList<DiscoveredLink>> FetchAsync(Source source, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(source.Config) ?? new();
        var feedUrl = config[FeedUrlKey];

        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
        var xml = await client.GetStringAsync(feedUrl, cancellationToken);

        return ParseFeed(xml);
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
