using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

/// <summary>
/// A preview exists to show a person what they are about to subscribe to. Interpreters leave
/// <see cref="DiscoveredLink.Title"/> null and put the feed's own title in metadata — ingestion
/// prefers the title the enricher later reads off the page — so without a fallback every RSS
/// preview would be a column of bare URLs.
/// </summary>
public class SourcePreviewTitleTests
{
    /// <summary>Returns whatever it was handed, so the test is about the mapping and nothing else.</summary>
    private sealed class StubInterpreter(IReadOnlyList<DiscoveredLink> links) : ISourceInterpreter
    {
        public SourceType Type => SourceType.Rss;

        public void ValidateConfig(IReadOnlyDictionary<string, string> config) { }

        public Task<SourceFetchResult> FetchAsync(
            IReadOnlyDictionary<string, string> config, string? state, CancellationToken ct = default) =>
            Task.FromResult(new SourceFetchResult(links, null));
    }

    /// <summary>
    /// Only <c>interpreters</c> is reachable from <see cref="SourceService.PreviewAsync"/>: a
    /// preview is a live fetch of a config nobody has saved, so it touches no database, no
    /// scheduler and no quota.
    /// </summary>
    private static SourceService ServiceOver(params DiscoveredLink[] links) =>
        new(null!, [new StubInterpreter(links)], null!, null!, null!, null!);

    private static PreviewSourceRequest Request() =>
        new(SourceType.Rss, new Dictionary<string, string> { ["feedUrl"] = "https://example.com/feed" });

    [Fact]
    public async Task The_feeds_own_title_is_shown_when_the_link_carries_none()
    {
        var service = ServiceOver(new DiscoveredLink(
            "https://example.com/1", null, new Dictionary<string, string> { ["title"] = "First Post" }));

        var preview = await service.PreviewAsync(Guid.NewGuid(), Request());

        Assert.Equal("First Post", preview.Links[0].Title);
    }

    [Fact]
    public async Task A_title_the_interpreter_set_itself_wins()
    {
        var service = ServiceOver(new DiscoveredLink(
            "https://example.com/1", "From the page", new Dictionary<string, string> { ["title"] = "From the feed" }));

        var preview = await service.PreviewAsync(Guid.NewGuid(), Request());

        // An interpreter that bothered to set Title looked at something better than feed metadata.
        Assert.Equal("From the page", preview.Links[0].Title);
    }

    [Fact]
    public async Task A_link_with_nothing_to_call_it_stays_untitled()
    {
        var service = ServiceOver(
            new DiscoveredLink("https://example.com/1", null, null),
            new DiscoveredLink("https://example.com/2", null, new Dictionary<string, string> { ["title"] = "   " }));

        var preview = await service.PreviewAsync(Guid.NewGuid(), Request());

        // The UI shows the URL in this case; a blank string would render as an empty row instead.
        Assert.Null(preview.Links[0].Title);
        Assert.Null(preview.Links[1].Title);
    }
}
