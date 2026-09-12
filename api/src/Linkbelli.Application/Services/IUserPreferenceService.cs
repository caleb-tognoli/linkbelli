namespace Linkbelli.Application.Services;

/// <summary>Everything one user has decided about how the app behaves for them.</summary>
/// <param name="ShowNsfw">Whether they opt in to adult content.</param>
/// <param name="ArchiveLinks">Whether they asked for public snapshots of the pages they save.</param>
/// <param name="BackupsEnabled">Whether the schedule keeps snapshots of their library.</param>
/// <param name="OnboardingDismissed">Whether the getting-started checklist has been put away.</param>
public record UserPreferences(
    bool ShowNsfw,
    bool ArchiveLinks,
    bool BackupsEnabled,
    bool OnboardingDismissed);

public interface IUserPreferenceService
{
    /// <summary>
    /// All of them in one read. Null/unknown user gets the defaults rather than an error.
    /// </summary>
    /// <remarks>
    /// Prefer this to calling several of the single-value methods below: they are each a query,
    /// and asking four questions of one row four times is what this exists to stop.
    /// </remarks>
    Task<UserPreferences> GetAsync(Guid? userId, CancellationToken ct = default);

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
