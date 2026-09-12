namespace Linkbelli.Application.Services;

public interface IUserPreferenceService
{
    /// <summary>Whether the user opts in to NSFW content. Null/unknown user → false.</summary>
    Task<bool> ShowNsfwAsync(Guid? userId, CancellationToken ct = default);

    Task SetShowNsfwAsync(Guid userId, bool showNsfw, CancellationToken ct = default);

    /// <summary>Whether the user asked for public snapshots of the pages they save.</summary>
    Task<bool> ArchiveLinksAsync(Guid? userId, CancellationToken ct = default);

    Task SetArchiveLinksAsync(Guid userId, bool archiveLinks, CancellationToken ct = default);

    /// <summary>Whether the schedule keeps snapshots of this user's library.</summary>
    Task<bool> BackupsEnabledAsync(Guid? userId, CancellationToken ct = default);

    Task SetBackupsEnabledAsync(Guid userId, bool backupsEnabled, CancellationToken ct = default);

    /// <summary>Whether the getting-started checklist has been put away.</summary>
    Task<bool> OnboardingDismissedAsync(Guid? userId, CancellationToken ct = default);

    Task DismissOnboardingAsync(Guid userId, CancellationToken ct = default);
}
