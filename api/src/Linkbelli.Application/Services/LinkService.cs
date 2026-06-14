using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Mapping;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Services;

public class LinkService(
    IAppDbContext db,
    ILinkEnrichmentQueue enrichmentQueue,
    ILinkEnricher enricher,
    LinkMetadataFetcher metadataFetcher,
    ILogger<LinkService> logger) : ILinkService
{
    public async Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (!UrlCanonicalizer.TryCanonicalize(request.Url, out var canonical))
        {
            throw new ValidationException("url", "A valid http(s) URL is required.");
        }

        var link = await GetOrCreateAsync(canonical, immediate: true, cancellationToken);
        return link.ToResponse();
    }

    public async Task<LinkPreviewResponse> PreviewAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!UrlCanonicalizer.TryCanonicalize(url, out var canonical))
        {
            throw new ValidationException("url", "A valid http(s) URL is required.");
        }

        var metadata = await metadataFetcher.TryFetchAsync(canonical.Url, cancellationToken);
        return new LinkPreviewResponse(
            canonical.Url, canonical.Host,
            metadata?.Title, metadata?.Description, metadata?.ImageUrl, metadata?.SiteName);
    }

    public async Task<Link> GetOrCreateAsync(CanonicalUrl canonical, bool immediate = false, CancellationToken cancellationToken = default)
    {
        var existing = await db.Links
            .Include(l => l.Host)
            .FirstOrDefaultAsync(l => l.UrlHash == canonical.Hash, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var host = await GetOrCreateHostAsync(canonical.Host, cancellationToken);
        var link = new Link
        {
            CanonicalUrl = canonical.Url,
            UrlHash = canonical.Hash,
            HostId = host.Id,
            Host = host,
        };
        db.Links.Add(link);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await EnrichNewLinkAsync(link, immediate, cancellationToken);
            return link;
        }
        catch (DbUpdateException)
        {
            // Lost a race on the unique UrlHash — the other writer's row is authoritative
            // (and already enqueued by the winner), so we don't enqueue again.
            db.Entry(link).State = EntityState.Detached;
            return await db.Links.Include(l => l.Host)
                .FirstAsync(l => l.UrlHash == canonical.Hash, cancellationToken);
        }
    }

    /// <summary>
    /// Enriches a just-created link. Manual adds (<paramref name="immediate"/>) enrich synchronously
    /// so the link appears right away; on a transient failure we fall back to the queue so it still
    /// gets enriched (and shown) later. Source ingestion always queues.
    /// </summary>
    private async Task EnrichNewLinkAsync(Link link, bool immediate, CancellationToken cancellationToken)
    {
        if (!immediate)
        {
            enrichmentQueue.Enqueue(link.Id);
            return;
        }

        try
        {
            await enricher.EnrichAsync(link.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Immediate enrichment failed for {LinkId}; queuing for retry.", link.Id);
            enrichmentQueue.Enqueue(link.Id);
        }
    }

    private async Task<Host> GetOrCreateHostAsync(string hostname, CancellationToken cancellationToken)
    {
        var existing = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname == hostname, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var host = new Host { Hostname = hostname };
        db.Hosts.Add(host);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return host;
        }
        catch (DbUpdateException)
        {
            db.Entry(host).State = EntityState.Detached;
            return await db.Hosts.FirstAsync(h => h.Hostname == hostname, cancellationToken);
        }
    }
}
