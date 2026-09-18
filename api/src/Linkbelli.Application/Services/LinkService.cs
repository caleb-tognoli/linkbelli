using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Mapping;
using Linkbelli.Application.Webhooks;
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
    IWebhookEvents webhooks,
    ILogger<LinkService> logger) : ILinkService
{
    public async Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken = default)
    {
        if (!UrlCanonicalizer.TryCanonicalize(request.Url, out var canonical))
        {
            throw new ValidationException("url", "A valid http(s) URL is required.");
        }

        var link = await GetOrCreateAsync(canonical, immediate: true, cancellationToken: cancellationToken);
        return link.ToResponse();
    }

    public async Task<LinkContentResponse> GetContentAsync(
        Guid ownerId, Guid linkId, CancellationToken cancellationToken = default)
    {
        // Ownership is by having saved it, not by owning the link: links are shared globally, so
        // the row alone says nothing about who may read the text out of it.
        var link = await db.Links
            .AsNoTracking()
            .Include(l => l.Host)
            .Where(l => l.Id == linkId
                && db.PlaylistItems.Any(i => i.LinkId == l.Id && i.Playlist!.OwnerId == ownerId))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Link not found.");

        if (string.IsNullOrEmpty(link.Content))
        {
            // Most pages are not articles, and the ones that are may have been saved before the
            // text was ever kept. Either way there is nothing to read here.
            throw new NotFoundException("No readable text was found on that page.");
        }

        // The furthest the caller got in any copy of this link. A link in two playlists is one
        // article, and the reader should open where the reading stopped whichever row was clicked.
        var progress = await db.PlaylistItems
            .Where(i => i.LinkId == linkId && i.Playlist!.OwnerId == ownerId && i.ReadProgress != null)
            .MaxAsync(i => i.ReadProgress, cancellationToken);

        return new LinkContentResponse(
            link.Id,
            link.CanonicalUrl,
            link.Host!.Hostname,
            link.Title,
            link.SiteName,
            link.Content.Split(ArticleExtractor.ParagraphSeparator, StringSplitOptions.RemoveEmptyEntries),
            link.WordCount ?? 0,
            link.ContentTruncated,
            progress);
    }

    /// <summary>
    /// How far down the article counts as having read it.
    /// </summary>
    /// <remarks>
    /// Not 1.0. Every article ends in a footer, a byline or a "related stories" block that
    /// nobody reads, so demanding the very bottom means the last step is always manual — which
    /// is the step this exists to remove.
    /// </remarks>
    public const double FinishedAt = 0.92;

    public async Task SetReadProgressAsync(
        Guid ownerId, Guid linkId, double progress, CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(progress))
        {
            throw new ValidationException("progress", "Progress has to be a number between 0 and 1.");
        }

        var clamped = Math.Clamp(progress, 0, 1);
        var now = DateTimeOffset.UtcNow;

        var mine = await db.PlaylistItems
            .Where(i => i.LinkId == linkId && i.Playlist!.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        if (mine.Count == 0)
        {
            // Not saved by this person. Reading is only ever offered from their own library, so
            // this is either a stale tab or somebody guessing at ids.
            throw new NotFoundException("Link not found.");
        }

        var finished = new List<Guid>();
        foreach (var item in mine)
        {
            // Never backwards. Scrolling up to re-read a paragraph is not un-reading it, and a
            // progress bar that retreats while you look at it is worse than none.
            item.ReadProgress = Math.Max(item.ReadProgress ?? 0, clamped);
            item.LastReadAt = now;

            if (clamped >= FinishedAt && item.Status == PlaylistItemStatus.Added)
            {
                // The interaction people expect: reaching the end of something is what finishing
                // it means, and making them say so as well is a step for the app's benefit.
                item.Status = PlaylistItemStatus.Watched;
                item.StatusChangedAt = now;
                finished.Add(item.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await webhooks.ItemsFinishedAsync(finished, cancellationToken);
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

    public async Task<Link> GetOrCreateAsync(CanonicalUrl canonical, bool immediate = false, string? initialTitle = null, CancellationToken cancellationToken = default)
    {
        var existing = await db.Links
            .Include(l => l.Host)
            .FirstOrDefaultAsync(l => l.UrlHash == canonical.Hash, cancellationToken);

        // Deliberately not matched against another link's ResolvedUrlHash, tempting as it looks.
        //
        // Doing so would mean: you save the address a page actually lives at, and get handed the
        // row somebody saved under a shortener, showing that shortener as the address. Even when
        // it is the same page that is the worse of the two addresses; and when it is not — a
        // paywall stub, a consent screen, a "this has moved" page, all of which several articles
        // redirect to — it is simply wrong, silently.
        //
        // The redirect is recorded at enrichment and offered on the duplicates page as something
        // to look at. A suggestion can be declined. This could not.
        if (existing is not null)
        {
            if (existing.Host!.Blocked)
            {
                throw new BlockedHostException($"Links from '{existing.Host.Hostname}' are blocked.");
            }

            return existing;
        }

        var host = await GetOrCreateHostAsync(canonical.Host, cancellationToken);
        if (host.Blocked)
        {
            throw new BlockedHostException($"Links from '{host.Hostname}' are blocked.");
        }

        var link = new Link
        {
            CanonicalUrl = canonical.Url,
            UrlHash = canonical.Hash,
            HostId = host.Id,
            Host = host,
            Title = string.IsNullOrWhiteSpace(initialTitle) ? null : initialTitle.Trim(),
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
