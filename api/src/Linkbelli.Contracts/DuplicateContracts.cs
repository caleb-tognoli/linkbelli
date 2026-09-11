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
