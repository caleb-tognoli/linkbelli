namespace Linkbelli.Core.Entities;

/// <summary>
/// One thing somebody did that is worth being able to look up afterwards.
/// </summary>
/// <remarks>
/// Admin actions reach into other people's data, and several user actions destroy something
/// outright. Neither left any trace: the only record that a host had been blocked, or that a
/// playlist full of links had been purged, was the absence of what used to be there.
/// </remarks>
public class AuditEntry : BaseEntity<Guid>
{
    /// <summary>Who did it. Null for something the system did on its own.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>
    /// Their name as it was at the time. Denormalized on purpose — an audit trail that stops
    /// naming people once their account goes is not an audit trail.
    /// </summary>
    public required string ActorName { get; set; }

    /// <summary>Whether they were acting as an admin, which is what makes it worth keeping.</summary>
    public bool AsAdmin { get; set; }

    /// <summary>What was done, as a stable dotted key: "playlist.delete", "admin.host.block".</summary>
    public required string Action { get; set; }

    /// <summary>What it was done to — "playlist", "source", "host", "link", "apikey".</summary>
    public string? TargetType { get; set; }

    public Guid? TargetId { get; set; }

    /// <summary>One line a person can read without decoding the details below.</summary>
    public string? Summary { get; set; }

    /// <summary>Before and after, as jsonb. Shapeless on purpose: every action has its own.</summary>
    public string? Details { get; set; }
}
