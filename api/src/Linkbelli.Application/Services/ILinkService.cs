using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;

namespace Linkbelli.Application.Services;

public interface ILinkService
{
    /// <summary>Resolves a canonical URL to its globally-deduplicated Link, creating it (and its Host) on first sight.</summary>
    Task<Link> GetOrCreateAsync(CanonicalUrl canonical, CancellationToken cancellationToken = default);

    /// <summary>Validates and canonicalizes a raw URL, then get-or-creates the Link and returns its DTO.</summary>
    Task<LinkResponse> CreateAsync(CreateLinkRequest request, CancellationToken cancellationToken = default);

    /// <summary>Canonicalizes a URL and fetches its metadata without saving (preview before save).</summary>
    Task<LinkPreviewResponse> PreviewAsync(string url, CancellationToken cancellationToken = default);
}
