namespace Linkbelli.Core.Playlists;

/// <summary>
/// Position math for the gap-based ordering of playlist items. Positions are spaced
/// by <see cref="Gap"/> so a move is usually a single-row update; only when two
/// neighbours are adjacent must the caller renumber the playlist.
/// </summary>
public static class PlaylistOrdering
{
    public const long Gap = 1024;

    /// <summary>
    /// The position to give an item placed between <paramref name="before"/> and
    /// <paramref name="after"/> (null = no neighbour on that side). Returns null when
    /// there is no integer room, signalling the caller to renumber.
    /// </summary>
    public static long? Between(long? before, long? after)
    {
        if (after is null)
        {
            return (before ?? 0) + Gap; // appending at the end (or first item)
        }

        var lower = before ?? 0;
        if (after.Value - lower <= 1)
        {
            return null; // no room — renumber required
        }

        return lower + (after.Value - lower) / 2;
    }
}
