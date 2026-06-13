using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyResponse>> ListAsync(Guid userId, CancellationToken ct = default);
    Task<CreateApiKeyResponse> CreateAsync(Guid userId, CreateApiKeyRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);
}
