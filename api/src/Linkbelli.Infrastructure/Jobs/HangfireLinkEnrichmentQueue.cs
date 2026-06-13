using Hangfire;
using Linkbelli.Application.Enrichment;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>Enqueues enrichment as a durable Hangfire background job.</summary>
public sealed class HangfireLinkEnrichmentQueue(IBackgroundJobClient jobs) : ILinkEnrichmentQueue
{
    public void Enqueue(Guid linkId) =>
        jobs.Enqueue<ILinkEnricher>(enricher => enricher.EnrichAsync(linkId, CancellationToken.None));
}
