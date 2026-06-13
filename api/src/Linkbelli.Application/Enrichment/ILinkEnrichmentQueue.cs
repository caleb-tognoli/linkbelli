namespace Linkbelli.Application.Enrichment;

/// <summary>Schedules a link for asynchronous metadata enrichment (backed by a job runner).</summary>
public interface ILinkEnrichmentQueue
{
    void Enqueue(Guid linkId);
}
