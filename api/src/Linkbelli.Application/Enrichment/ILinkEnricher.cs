namespace Linkbelli.Application.Enrichment;

/// <summary>Fetches a link's page and fills its metadata (title, description, thumbnail, …).</summary>
public interface ILinkEnricher
{
    Task EnrichAsync(Guid linkId, CancellationToken cancellationToken = default);
}
