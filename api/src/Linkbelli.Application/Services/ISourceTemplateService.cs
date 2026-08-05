using Linkbelli.Contracts;

namespace Linkbelli.Application.Services;

public interface ISourceTemplateService
{
    /// <summary>Returns own templates + saved templates + app (system) templates for the gallery.</summary>
    Task<IReadOnlyList<SourceTemplateSummary>> ListAsync(Guid callerId, CancellationToken ct = default);

    /// <summary>Returns all templates (admin view, includes private and all owners).</summary>
    Task<IReadOnlyList<SourceTemplateResponse>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Discover Public/Unlisted templates owned by other users.</summary>
    Task<IReadOnlyList<SourceTemplateSummary>> DiscoverAsync(Guid callerId, string? q, string[]? tags, CancellationToken ct = default);

    Task<SourceTemplateResponse> GetAsync(Guid id, Guid callerId, bool isAdmin, CancellationToken ct = default);
    Task<SourceTemplateResponse> CreateAsync(Guid callerId, bool isAdmin, CreateSourceTemplateRequest request, CancellationToken ct = default);
    Task<SourceTemplateResponse> UpdateAsync(Guid id, Guid callerId, bool isAdmin, UpdateSourceTemplateRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid callerId, bool isAdmin, CancellationToken ct = default);

    Task SaveAsync(Guid templateId, Guid userId, CancellationToken ct = default);
    Task UnsaveAsync(Guid templateId, Guid userId, CancellationToken ct = default);
}
