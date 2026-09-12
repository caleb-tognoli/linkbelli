namespace Linkbelli.Contracts;

/// <summary>Update the caller's preferences.</summary>
/// <summary>
/// Preferences. Every field is optional and an omitted one is left alone, so a screen that owns
/// one setting can save it without stating a position on the others — and without a client that
/// predates a setting silently turning it off for someone who asked for it.
/// </summary>
public record UpdatePreferencesRequest(bool? ShowNsfw = null, bool? ArchiveLinks = null, bool? BackupsEnabled = null);

/// <summary>One snapshot, as a listing shows it. The bytes are fetched separately.</summary>
public record BackupResponse(
    Guid Id,
    DateTimeOffset TakenAt,
    int SizeBytes,
    int PlaylistCount,
    int ItemCount,
    bool Automatic);
