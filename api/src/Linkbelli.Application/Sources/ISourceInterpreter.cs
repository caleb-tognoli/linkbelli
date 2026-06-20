using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>A link discovered by a source: its URL, an optional title hint, and source-provided metadata.</summary>
public record DiscoveredLink(string Url, string? Title, IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Outcome of one fetch: the discovered links, optional updated interpreter state to persist
/// (ETag/Last-Modified/cursor…), and whether the upstream reported "not modified" (304).
/// </summary>
public record SourceFetchResult(
    IReadOnlyList<DiscoveredLink> Links,
    string? State = null,
    bool NotModified = false);

/// <summary>
/// Interprets one declarative source type, turning its config into discovered links.
/// One implementation per <see cref="SourceType"/>.
/// </summary>
public interface ISourceInterpreter
{
    SourceType Type { get; }

    /// <summary>Validates a source's config; throws ValidationException if invalid.</summary>
    void ValidateConfig(IReadOnlyDictionary<string, string> config);

    /// <summary>
    /// Whether a config key holds a secret. Secret values are encrypted at rest and redacted
    /// in API responses. Defaults to false (no secrets).
    /// </summary>
    bool IsSecretConfigKey(string key) => false;

    /// <summary>
    /// Fetches links using already-decrypted <paramref name="config"/> and the prior persisted
    /// <paramref name="state"/> (may be null). Returns discovered links plus any new state.
    /// </summary>
    Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config,
        string? state,
        CancellationToken cancellationToken = default);
}
