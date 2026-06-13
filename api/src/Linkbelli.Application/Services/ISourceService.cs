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
}
