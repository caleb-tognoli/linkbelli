namespace Linkbelli.Contracts;

/// <summary>Update the caller's preferences.</summary>
/// <summary>
/// Preferences. Every field is optional and an omitted one is left alone, so a screen that owns
/// one setting can save it without stating a position on the others — and without a client that
/// predates a setting silently turning it off for someone who asked for it.
/// </summary>
public record UpdatePreferencesRequest(
    bool? ShowNsfw = null,
    bool? ArchiveLinks = null,
    bool? BackupsEnabled = null,
    /// <summary>True puts the getting-started checklist away for good; false is not a way back.</summary>
    bool? DismissOnboarding = null);

/// <summary>One snapshot, as a listing shows it. The bytes are fetched separately.</summary>
public record BackupResponse(
    Guid Id,
    DateTimeOffset TakenAt,
    int SizeBytes,
    int PlaylistCount,
    int ItemCount,
    bool Automatic);

/// <summary>What Linkbelli will email this account about.</summary>
public record NotificationPreferencesResponse(
    bool OnShare,
    bool OnFollow,
    bool OnSourceStopped,
    bool WeeklyDigest);

/// <summary>
/// Changes some of them. Every field is optional and an omitted one is left alone, so a screen
/// that owns one switch can save it without deciding about the rest.
/// </summary>
public record UpdateNotificationsRequest(
    bool? OnShare = null,
    bool? OnFollow = null,
    bool? OnSourceStopped = null,
    bool? WeeklyDigest = null);

/// <summary>The token out of an unsubscribe link. It names both the account and the kind.</summary>
public record UnsubscribeRequest(string Token);
