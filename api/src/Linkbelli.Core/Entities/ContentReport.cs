namespace Linkbelli.Core.Entities;

/// <summary>Why something was reported. A short list, because a long one gets picked at random.</summary>
public enum ReportReason
{
    Other = 0,
    Spam = 1,
    /// <summary>Adult content that isn't flagged as such.</summary>
    Nsfw = 2,
    Malware = 3,
    /// <summary>Unlawful where this instance is run.</summary>
    Illegal = 4,
    /// <summary>Someone else's work, published without them.</summary>
    Copyright = 5,
}

public enum ReportStatus
{
    Open = 0,
    /// <summary>Looked at, and something was done.</summary>
    Resolved = 1,
    /// <summary>Looked at, and nothing needed doing.</summary>
    Dismissed = 2,
}

/// <summary>
/// Somebody telling the people who run this instance that something published here is wrong.
/// </summary>
/// <remarks>
/// Moderation was a host blocklist and nothing else: a visitor who found something had no way to
/// say so, and whoever runs the instance had no way to hear it.
/// </remarks>
public class ContentReport : BaseEntity<Guid>
{
    /// <summary>Who reported it. Reporting needs an account, or the queue fills with noise.</summary>
    public Guid ReporterId { get; set; }

    /// <summary>The playlist complained about.</summary>
    public Guid PlaylistId { get; set; }

    public ReportReason Reason { get; set; } = ReportReason.Other;

    /// <summary>What they wanted to say beyond the reason. Often the only useful part.</summary>
    public string? Note { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Open;

    public Guid? ResolvedBy { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>What was done about it, for the next person who looks at the same playlist.</summary>
    public string? Resolution { get; set; }

    public Playlist? Playlist { get; set; }
}
