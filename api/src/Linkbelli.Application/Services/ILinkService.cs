using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;

namespace Linkbelli.Application.Services;

public interface ILinkService
{
    /// <summary>
    /// Resolves a canonical URL to its globally-deduplicated Link, creating it (and its Host) on
    /// first sight. <paramref name="immediate"/> enriches new links synchronously (manual adds),
    /// otherwise enrichment is queued (source ingestion). <paramref name="initialTitle"/> seeds the
    /// Link's title on creation (source-provided titles from APIs like TMDB); enrichment then only
    /// fills in the title when it's still empty, so a curated source title isn't overwritten.
    /// </summary>
    Task<Link> GetOrCreateAsync(CanonicalUrl canonical, bool immediate = false, string? initialTitle = null, CancellationToken cancellationToken = default);

    /// <summary>Validates and canonicalizes a raw URL, then get-or-creates the Link and returns its DTO.</summary>
    Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>Canonicalizes a URL and fetches its metadata without saving (preview before save).</summary>
    Task<LinkPreviewResponse> PreviewAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored article text, for someone who has this link in one of their own playlists.
    /// Links are global, so the text is only handed to people who actually saved the page.
    /// </summary>
    Task<LinkContentResponse> GetContentAsync(Guid ownerId, Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records how far through an article somebody got, and marks it finished at the end.
    /// </summary>
    /// <remarks>
    /// Written to every copy the caller has of that link. You read the article, not the row —
    /// and a link in two playlists that stayed half-read in one of them would be a worse lie
    /// than the two-state flag this replaces.
    /// </remarks>
    Task SetReadProgressAsync(
        Guid ownerId, Guid linkId, double progress, CancellationToken cancellationToken = default);
}
