namespace Linkbelli.Contracts;

/// <summary>Why a set of saved links is considered the same thing.</summary>
public enum DuplicateKind
{
    /// <summary>The identical link, saved into more than one playlist.</summary>
    SameLink,

    /// <summary>
    /// The same page reached by different addresses — same host and path, different query.
    /// Canonicalization strips known tracking parameters, but not the ones it has never seen.
    /// </summary>
    SamePage,

    /// <summary>
    /// Different addresses that the fetch found leading to the same page.
    /// </summary>
    /// <remarks>
    /// A shortener, an <c>m.</c> subdomain, a renamed article slug, <c>?amp=1</c>: the host and
    /// path share nothing, so the grouping above cannot see it, and the enricher followed the
    /// redirect without writing down where it went.
    ///
    /// A suggestion, not a verdict. A consent interstitial, a paywall and a "this has moved"
    /// stub all land somewhere shared without being the same page, so this names the candidates
    /// and leaves the decision to whoever saved them.
    /// </remarks>
    SameAfterRedirect,
}

/// <summary>One saved copy: where it lives, and what it is.</summary>
public record DuplicateCopy(
    Guid ItemId,
    Guid PlaylistId,
    string PlaylistName,
    string Url,
    string? Title,
    DateTimeOffset AddedAt);

/// <summary>
/// A set of saved links that are the same thing. Two or more copies, always — a single copy is
/// not a duplicate of anything.
/// </summary>
public record DuplicateGroup(
    DuplicateKind Kind,
    /// <summary>What the copies have in common: the canonical URL, or the host and path.</summary>
    string Key,
    IReadOnlyList<DuplicateCopy> Copies);
