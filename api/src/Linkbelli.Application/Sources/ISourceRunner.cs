namespace Linkbelli.Application.Sources;

/// <summary>Executes one run of a source: fetch → dedup → append to attached playlists → log.</summary>
public interface ISourceRunner
{
    Task RunAsync(Guid sourceId, CancellationToken cancellationToken = default);
}
