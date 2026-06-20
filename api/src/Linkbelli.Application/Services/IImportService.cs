using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface IImportService
{
    Task<ImportResult> ImportAsync(Guid ownerId, ImportRequest request, CancellationToken ct = default);
}
