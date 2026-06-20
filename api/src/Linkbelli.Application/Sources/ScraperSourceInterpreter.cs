using System.Net.Http;
using AngleSharp.Html.Parser;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Scrapes a web page for links via CSS selectors (through the SSRF-protected client).
/// Config: { url, itemSelector, linkAttribute?, titleSelector?, header.* }.
/// <c>itemSelector</c> selects the link-bearing elements; <c>linkAttribute</c> (default
/// <c>href</c>) holds each URL; <c>titleSelector</c> (optional, searched within each element)
/// or the element's text supplies the title. Relative URLs resolve against <c>url</c>.
/// Keys prefixed <c>header.</c> become request headers and are treated as secrets (encrypted
/// at rest) — use <c>header.Cookie</c> for session-authenticated pages.
/// </summary>
public sealed class ScraperSourceInterpreter(IHttpClientFactory httpClientFactory) : ISourceInterpreter
{
    public const string UrlKey = "url";
    public const string ItemSelectorKey = "itemSelector";
    public const string LinkAttributeKey = "linkAttribute";
    public const string TitleSelectorKey = "titleSelector";
    public const string HeaderPrefix = "header.";
    private const int MaxItemsPerRun = 100;
    private static readonly HtmlParser Parser = new();

    public SourceType Type => SourceType.Scraper;

    public bool IsSecretConfigKey(string key) => key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase);

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
        using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
        foreach (var (key, value) in config)
        {
            if (key.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
            {
                request.Headers.TryAddWithoutValidation(key[HeaderPrefix.Length..], value);
            }
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
        var linkAttribute = config.GetValueOrDefault(LinkAttributeKey) is { Length: > 0 } a ? a : "href";
        var titleSelector = config.GetValueOrDefault(TitleSelectorKey);
        var baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? b : null;

        var doc = Parser.ParseDocument(html);
        var results = new List<DiscoveredLink>();
        var seen = new HashSet<string>();
        foreach (var el in doc.QuerySelectorAll(itemSelector))
        {
            var raw = el.GetAttribute(linkAttribute);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var resolved = baseUri is not null && Uri.TryCreate(baseUri, raw, out var abs) ? abs.ToString() : raw.Trim();
            if (!seen.Add(resolved))
            {
                continue;
            }

            string? title = null;
            if (!string.IsNullOrWhiteSpace(titleSelector))
            {
                title = el.QuerySelector(titleSelector)?.TextContent.Trim();
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = string.IsNullOrWhiteSpace(el.TextContent) ? null : el.TextContent.Trim();
            }

            results.Add(new DiscoveredLink(resolved, string.IsNullOrWhiteSpace(title) ? null : title));
            if (results.Count >= MaxItemsPerRun)
            {
                break;
            }
        }

        return results;
    }
}
