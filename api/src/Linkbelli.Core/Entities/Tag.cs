namespace Linkbelli.Core.Entities;

/// <summary>
/// A free-text label, globally deduplicated by normalized <see cref="Name"/> (lowercase).
/// Get-or-created on first use, like <see cref="Host"/>.
/// </summary>
public class Tag : BaseEntity<Guid>
{
    /// <summary>Normalized (trimmed, lowercased) label; unique across the system.</summary>
    public required string Name { get; set; }

    public List<PlaylistTag> Playlists { get; set; } = [];
}
