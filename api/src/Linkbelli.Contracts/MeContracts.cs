namespace Linkbelli.Contracts;

/// <summary>Update the caller's preferences.</summary>
/// <summary>
/// Preferences. <c>ArchiveLinks</c> is optional so an older client that only knows about
/// <c>ShowNsfw</c> doesn't silently turn archiving off for someone who asked for it.
/// </summary>
public record UpdatePreferencesRequest(bool ShowNsfw, bool? ArchiveLinks = null);
