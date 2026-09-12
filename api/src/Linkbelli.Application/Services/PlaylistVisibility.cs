using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Services;

/// <summary>
/// Whether a playlist counts as adult, and who should therefore be shown it.
/// </summary>
/// <remarks>
/// The rule is two-part: the owner's explicit flag if they set one, and otherwise whatever the
/// items say. It was written out longhand in nine places across PlaylistService.
///
/// The five that filter are collapsed here. The other four are projections — they put the answer
/// in a response rather than narrowing a query — and EF cannot splice a stored expression into a
/// projection, so those still spell it out. Worth knowing if the rule ever changes: this file is
/// the first place to look, and not the only one.
/// </remarks>
public static class PlaylistNsfw
{
    /// <summary>
    /// Drops adult playlists unless the viewer opted in.
    /// </summary>
    /// <remarks>
    /// A no-op when they have, so callers can apply it unconditionally instead of each one
    /// deciding whether to.
    /// </remarks>
    public static IQueryable<Playlist> VisibleTo(this IQueryable<Playlist> playlists, bool showNsfw) =>
        showNsfw
            ? playlists
            : playlists.Where(p => !(p.NsfwOverride != null
                ? p.NsfwOverride.Value
                : p.Items.Any(i => i.Link!.Nsfw)));
}
