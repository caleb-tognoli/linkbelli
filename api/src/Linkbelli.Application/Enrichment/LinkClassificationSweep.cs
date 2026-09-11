using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Core.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

/// <summary>Classifies links saved before anything was asking what they were.</summary>
public interface ILinkClassificationSweep
{
    /// <summary>Classifies a batch of unclassified links. Returns how many it settled.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Everything it needs is already on the row — the address, the stored OpenGraph bag and the word
/// count — so no page is fetched and nothing is re-read. It runs the same classifier enrichment
/// does rather than a second copy of the rules in SQL, which would drift the first time either
/// changed.
/// </remarks>
public sealed class LinkClassificationSweep(
    IAppDbContext db,
    ILogger<LinkClassificationSweep> logger) : ILinkClassificationSweep
{
    /// <summary>
    /// Links per run. The sweep is repeated rather than long: a one-shot pass over a large
    /// collection would hold a transaction open behind every other write in the app.
    /// </summary>
    public const int BatchSize = 500;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var links = await db.Links
            .Include(l => l.Host)
            .Where(l => l.Kind == ContentKind.Unknown && l.EnrichedAt != null)
            .OrderBy(l => l.CreationTime)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
        {
            return 0;
        }

        var classified = 0;
        foreach (var link in links)
        {
            var kind = ContentClassifier.Classify(
                link.CanonicalUrl,
                link.Host?.Hostname ?? string.Empty,
                OgType(link.Metadata),
                mediaType: null,
                link.WordCount);

            if (kind == ContentKind.Unknown)
            {
                // Genuinely unclassifiable, and it will be looked at again on every sweep. That
                // is the cost of not having a second "tried and failed" flag for a query that
                // only runs once a night.
                continue;
            }

            link.Kind = kind;
            classified++;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Classified {Count} of {Examined} links.", classified, links.Count);

        return classified;
    }

    /// <summary>The page's own <c>og:type</c>, out of the metadata bag kept at enrichment.</summary>
    private static string? OgType(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);
            return raw?.GetValueOrDefault("og:type");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
