using System.Net.Http;
using AngleSharp.Html.Parser;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Scrapes a web page for links via CSS selectors (through the SSRF-protected client).
/// Config: { url, itemSelector, linkSelector?, linkAttribute?, meta.*, header.*, auth.* }.
/// <c>itemSelector</c> selects each item container. Within each item, <c>linkSelector</c>
/// (optional) finds the element that carries the URL; <c>linkAttribute</c> names the attribute
/// to read from that element (default <c>href</c>; empty = read text content as URL).
/// If <c>linkSelector</c> is absent the URL is read from the item element itself.
/// Metadata keys follow the pattern <c>meta.&lt;name&gt;</c> (CSS selector within item) and
/// <c>meta.&lt;name&gt;.attr</c> (attribute to read; absent = text content).
/// Keys prefixed <c>header.</c> become request headers (encrypted at rest).
/// Keys prefixed <c>auth.</c> trigger a pre-run credential login (also encrypted at rest).
/// </summary>
public sealed class ScraperSourceInterpreter(IHttpClientFactory httpClientFactory) : ISourceInterpreter
{
    public const string UrlKey = "url";
    public const string ItemSelectorKey = "itemSelector";
    public const string LinkSelectorKey = "linkSelector";
    public const string LinkAttributeKey = "linkAttribute";
    public const string MetaPrefix = "meta.";
    public const string HeaderPrefix = "header.";
    private const int MaxItemsPerRun = 100;
    private static readonly HtmlParser Parser = new();

    public SourceType Type => SourceType.Scraper;

    public bool IsSecretConfigKey(string key) =>
        key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) ||
        AuthLogin.IsSecretKey(key);

    public void ValidateConfig(IReadOnlyDictionary<string, string> config)
    {
        if (!config.TryGetValue(UrlKey, out var url) || !UrlCanonicalizer.TryCanonicalize(url, out _))
        {
            throw new ValidationException($"config.{UrlKey}", "A valid http(s) page URL is required.");
        }

        if (!config.TryGetValue(ItemSelectorKey, out var selector) || string.IsNullOrWhiteSpace(selector))
        {
            throw new ValidationException($"config.{ItemSelectorKey}", "A CSS selector for items is required.");
        }
    }

    public async Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
    {
        var pageUrl = config[UrlKey];
        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);

        var freshCookie = await AuthLogin.TryLoginAsync(config, client, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
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
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SourceFetchResult(Parse(html, pageUrl, config));
    }

    /// <summary>Pure HTML → links extraction; unit-testable from fixtures.</summary>
    public static IReadOnlyList<DiscoveredLink> Parse(string html, string baseUrl, IReadOnlyDictionary<string, string> config)
    {
        var itemSelector = config[ItemSelectorKey];
        var linkSelectorVal = config.GetValueOrDefault(LinkSelectorKey);
        var linkAttributeVal = config.GetValueOrDefault(LinkAttributeKey);
        var baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? b : null;

        // Collect meta.<name> selectors (keys without a dot after the prefix are field names).
        var metaSelectors = config
            .Where(kv => kv.Key.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase)
                      && !kv.Key[MetaPrefix.Length..].Contains('.'))
            .Select(kv => (
                name: kv.Key[MetaPrefix.Length..],
                selector: kv.Value,
                attr: config.GetValueOrDefault(kv.Key + ".attr")))
            .Where(m => !string.IsNullOrWhiteSpace(m.selector))
            .ToList();

        var doc = Parser.ParseDocument(html);
        var results = new List<DiscoveredLink>();
        var seen = new HashSet<string>();

        foreach (var el in doc.QuerySelectorAll(itemSelector))
        {
            // Resolve which element holds the URL.
            string? raw;
            if (!string.IsNullOrEmpty(linkSelectorVal))
            {
                var linkEl = el.QuerySelector(linkSelectorVal);
                if (linkEl is null) continue;
                raw = string.IsNullOrEmpty(linkAttributeVal)
                    ? linkEl.TextContent?.Trim()
                    : linkEl.GetAttribute(linkAttributeVal);
            }
            else
            {
                var effectiveAttr = string.IsNullOrEmpty(linkAttributeVal) ? "href" : linkAttributeVal;
                raw = el.GetAttribute(effectiveAttr);
            }

            if (string.IsNullOrWhiteSpace(raw)) continue;

            var resolved = baseUri is not null && Uri.TryCreate(baseUri, raw, out var abs) ? abs.ToString() : raw.Trim();
            if (!seen.Add(resolved)) continue;

            // Extract metadata fields.
            Dictionary<string, string>? metadata = null;
            foreach (var (fieldName, selector, attr) in metaSelectors)
            {
                var metaEl = el.QuerySelector(selector);
                if (metaEl is null) continue;
                var value = string.IsNullOrEmpty(attr)
                    ? metaEl.TextContent?.Trim()
                    : metaEl.GetAttribute(attr)?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    metadata ??= new Dictionary<string, string>();
                    metadata[fieldName] = value;
                }
            }

            results.Add(new DiscoveredLink(resolved, null, metadata));
            if (results.Count >= MaxItemsPerRun) break;
        }

        return results;
    }
}
