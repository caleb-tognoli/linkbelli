namespace Linkbelli.Application.Services;

public interface IUserPreferenceService
{
    /// <summary>Whether the user opts in to NSFW content. Null/unknown user → false.</summary>
    Task<bool> ShowNsfwAsync(Guid? userId, CancellationToken ct = default);

    Task SetShowNsfwAsync(Guid userId, bool showNsfw, CancellationToken ct = default);
}
