using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Http;

namespace Linkbelli.Tests;

/// <summary>
/// Lets the enricher run without waiting on anything. The real throttle serialises fetches per
/// host, which matters against a live origin and nowhere else.
/// </summary>
internal sealed class NullThrottle : IHostThrottle
{
    public Task WaitAsync(string hostname, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Hands the enricher one prepared client, and refuses anything else by name — so a test that
/// silently stopped exercising the path it meant to fails rather than passes.
/// </summary>
internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => name == EnrichmentHttpClient.Name ? client : throw new InvalidOperationException($"unexpected client: {name}");
}
