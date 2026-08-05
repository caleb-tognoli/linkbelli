using System.Net.Http;
using System.Text.RegularExpressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Newtonsoft.Json.Linq;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Fetches a JSON API and extracts links via JSONPath (through the SSRF-protected client).
/// Config: { url, itemsPath, urlPath?, urlTemplate?, titlePath?, header.*, auth.* }. <c>itemsPath</c>
/// selects the item nodes; <c>urlPath</c>/<c>titlePath</c> are JSONPaths evaluated relative to each
/// item. Either <c>urlPath</c> or <c>urlTemplate</c> must be set. <c>urlTemplate</c> builds each
/// URL from item fields via <c>{jsonpath}</c> placeholders — e.g.
/// <c>https://site.tld/movie/{id}</c> or <c>https://site.tld/{lang}/movie/{id}/{slug}</c>. Each
/// placeholder is evaluated per-item; if any resolves empty, the item is skipped. Keys prefixed
/// <c>header.</c> become request headers (encrypted at rest). Keys prefixed <c>auth.</c> trigger a
/// pre-run credential login (also encrypted): set <c>auth.loginUrl</c>, <c>auth.username</c>,
/// <c>auth.password</c>; the resulting session cookies replace <c>header.Cookie</c> for that run.
/// </summary>
public sealed class JsonApiSourceInterpreter(IHttpClientFactory httpClientFactory) : ISourceInterpreter
{
    public const string UrlKey = "url";
    public const string ItemsPathKey = "itemsPath";
    public const string UrlPathKey = "urlPath";
    public const string UrlTemplateKey = "urlTemplate";
    public const string TitlePathKey = "titlePath";
    public const string HeaderPrefix = "header.";
    private static readonly Regex PlaceholderRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled);
    private const int MaxItemsPerRun = 100;

    public SourceType Type => SourceType.JsonApi;

    public bool IsSecretConfigKey(string key) =>
        key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) ||
        AuthLogin.IsSecretKey(key);

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

        var hasUrlPath = config.TryGetValue(UrlPathKey, out var urlPath) && !string.IsNullOrWhiteSpace(urlPath);
        var hasUrlTemplate = config.TryGetValue(UrlTemplateKey, out var urlTemplate) && !string.IsNullOrWhiteSpace(urlTemplate);

        if (!hasUrlPath && !hasUrlTemplate)
        {
            throw new ValidationException(
                $"config.{UrlPathKey}",
                $"Either {UrlPathKey} or {UrlTemplateKey} is required.");
        }

        if (hasUrlTemplate && !PlaceholderRegex.IsMatch(urlTemplate!))
        {
            throw new ValidationException(
                $"config.{UrlTemplateKey}",
                "urlTemplate must contain at least one '{jsonpath}' placeholder.");
        }
    }

    public async Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
    {
        var apiUrl = config[UrlKey];
        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);

        var freshCookie = await AuthLogin.TryLoginAsync(config, client, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        foreach (var (key, value) in config)
        {
            if (key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                request.Headers.TryAddWithoutValidation(key[HeaderPrefix.Length..], value);
            }
        }

        if (freshCookie is not null)
        {
            request.Headers.Remove("Cookie");
            request.Headers.TryAddWithoutValidation("Cookie", freshCookie);
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
        var urlPath = config.GetValueOrDefault(UrlPathKey);
        var urlTemplate = config.GetValueOrDefault(UrlTemplateKey);
        var titlePath = config.GetValueOrDefault(TitlePathKey);

        var root = JToken.Parse(json);
        var results = new List<DiscoveredLink>();
        foreach (var item in root.SelectTokens(itemsPath))
        {
            string? url;
            if (!string.IsNullOrWhiteSpace(urlTemplate))
            {
                url = ExpandTemplate(urlTemplate, item);
            }
            else
            {
                url = item.SelectToken(urlPath!)?.ToString();
            }

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

    /// <summary>
    /// Replaces every <c>{jsonpath}</c> in <paramref name="template"/> with the item's value at that
    /// JSONPath. Returns null if any placeholder resolves to null/empty so the caller skips the item.
    /// </summary>
    private static string? ExpandTemplate(string template, JToken item)
    {
        var missing = false;
        var expanded = PlaceholderRegex.Replace(template, match =>
        {
            var value = item.SelectToken(match.Groups[1].Value)?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                missing = true;
                return string.Empty;
            }

            return value.Trim();
        });

        return missing ? null : expanded;
    }
}
