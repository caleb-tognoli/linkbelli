using System.Net.Http;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Newtonsoft.Json.Linq;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Fetches a JSON API and extracts links via JSONPath (through the SSRF-protected client).
/// Config: { url, itemsPath, urlPath, titlePath?, header.* }. <c>itemsPath</c> selects the
/// item nodes; <c>urlPath</c>/<c>titlePath</c> are evaluated relative to each item. Keys
/// prefixed <c>header.</c> become request headers and are treated as secrets (encrypted at rest).
/// </summary>
public sealed class JsonApiSourceInterpreter(IHttpClientFactory httpClientFactory) : ISourceInterpreter
{
    public const string UrlKey = "url";
    public const string ItemsPathKey = "itemsPath";
    public const string UrlPathKey = "urlPath";
    public const string TitlePathKey = "titlePath";
    public const string HeaderPrefix = "header.";
    private const int MaxItemsPerRun = 100;

    public SourceType Type => SourceType.JsonApi;

    public bool IsSecretConfigKey(string key) => key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase);

    public void ValidateConfig(IReadOnlyDictionary<string, string> config)
    {
        if (!config.TryGetValue(UrlKey, out var url) || !UrlCanonicalizer.TryCanonicalize(url, out _))
        {
            throw new ValidationException($"config.{UrlKey}", "A valid http(s) API URL is required.");
        }

        if (!config.TryGetValue(ItemsPathKey, out var items) || string.IsNullOrWhiteSpace(items))
        {
            throw new ValidationException($"config.{ItemsPathKey}", "A JSONPath to the items array is required.");
        }

        if (!config.TryGetValue(UrlPathKey, out var urlPath) || string.IsNullOrWhiteSpace(urlPath))
        {
            throw new ValidationException($"config.{UrlPathKey}", "A JSONPath to each item's URL is required.");
        }
    }

    public async Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
    {
        var apiUrl = config[UrlKey];
        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        foreach (var (key, value) in config)
        {
            if (key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                request.Headers.TryAddWithoutValidation(key[HeaderPrefix.Length..], value);
            }
        }

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return new SourceFetchResult(Parse(json, config));
    }

    /// <summary>Pure JSON → links extraction via JSONPath; unit-testable from fixtures.</summary>
    public static IReadOnlyList<DiscoveredLink> Parse(string json, IReadOnlyDictionary<string, string> config)
    {
        var itemsPath = config[ItemsPathKey];
        var urlPath = config[UrlPathKey];
        var titlePath = config.GetValueOrDefault(TitlePathKey);

        var root = JToken.Parse(json);
        var results = new List<DiscoveredLink>();
        foreach (var item in root.SelectTokens(itemsPath))
        {
            var url = item.SelectToken(urlPath)?.ToString();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(titlePath) ? null : item.SelectToken(titlePath)?.ToString();
            results.Add(new DiscoveredLink(url.Trim(), string.IsNullOrWhiteSpace(title) ? null : title!.Trim()));
            if (results.Count >= MaxItemsPerRun)
            {
                break;
            }
        }

        return results;
    }
}
