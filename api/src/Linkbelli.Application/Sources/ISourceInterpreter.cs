using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>A link discovered by a source: its URL and an optional title hint.</summary>
public record DiscoveredLink(string Url, string? Title);

/// <summary>
/// Interprets one declarative source type, turning its config into discovered links.
/// One implementation per <see cref="SourceType"/>.
/// </summary>
public interface ISourceInterpreter
{
    SourceType Type { get; }

    /// <summary>Validates a source's config; throws ValidationException if invalid.</summary>
    void ValidateConfig(IReadOnlyDictionary<string, string> config);

    Task<IReadOnlyList<DiscoveredLink>> FetchAsync(Source source, CancellationToken cancellationToken = default);
}
