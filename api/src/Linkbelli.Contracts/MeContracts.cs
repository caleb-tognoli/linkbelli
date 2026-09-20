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

/// <summary>
/// What a restore would do, or did.
/// </summary>
/// <remarks>
/// The same shape for both, because the only way to trust a restore is to be told what it will do
/// while it is still possible to decide otherwise — and then to be told what it actually did.
/// </remarks>
/// <param name="DryRun">True when nothing was written.</param>
/// <param name="FormatVersion">The shape of the file this came from.</param>
/// <param name="TakenAt">When the snapshot was made.</param>
/// <param name="PlaylistsMatched">Playlists already here, matched by slug and left alone.</param>
/// <param name="ItemsAlreadyThere">
/// Links the playlist already held. Left exactly as they are — a restore should not undo the
/// reading somebody has done since the snapshot.
/// </param>
/// <param name="Truncated">True when the restore hit its ceiling and stopped part way.</param>
/// <param name="SourcesNeedCredentials">
/// True when sources were recreated. Their config came out of the export with its secrets
/// redacted, so they arrive paused and may need credentials typed in again.
/// </param>
public record RestorePlan(
    bool DryRun,
    int FormatVersion,
    DateTimeOffset TakenAt,
    int FoldersAdded,
    int PlaylistsAdded,
    int PlaylistsMatched,
    int ItemsAdded,
    int ItemsAlreadyThere,
    int SourcesAdded,
    bool Truncated,
    bool SourcesNeedCredentials,
    /// <summary>
    /// Marked passages put back. Only onto articles that are saved here once the restore is
    /// done — a highlight is a mark in something you kept, and has nowhere to go otherwise.
    /// </summary>
    int HighlightsAdded = 0);

/// <summary>Restore from a file rather than a stored snapshot.</summary>
public record RestoreFromFileRequest(string Json, bool DryRun = false);

/// <summary>
/// Changes the password of the account making the request.
/// </summary>
/// <remarks>
/// The current password is required. A bearer token is proof that a session exists, not proof
/// that the person at the keyboard owns it — an unlocked laptop is the case this is for.
/// </remarks>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
