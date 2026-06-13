using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface ISourceService
{
    Task<IReadOnlyList<SourceResponse>> ListAsync(Guid ownerId, CancellationToken ct = default);
    Task<SourceResponse> GetAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<SourceResponse> CreateAsync(Guid ownerId, CreateSourceRequest request, CancellationToken ct = default);
    Task<SourceResponse> UpdateAsync(Guid ownerId, Guid id, UpdateSourceRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task RunNowAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SourceRunResponse>> ListRunsAsync(Guid ownerId, Guid id, CancellationToken ct = default);
    Task<PreviewSourceResponse> PreviewAsync(Guid ownerId, PreviewSourceRequest request, CancellationToken ct = default);

    /// <summary>Browse shared sources (any owner) so they can be subscribed to a playlist.</summary>
    Task<IReadOnlyList<SharedSourceSummary>> ListSharedAsync(string? q, CancellationToken ct = default);
}
