namespace Linkbelli.Core.Entities;

/// <summary>
/// A snapshot of everything one user owned at a moment, kept so it can be handed back later.
/// </summary>
/// <remarks>
/// Export has always existed, but it only helps someone who thought to run it. The people who
/// most need a copy of their library are the ones who never made one. This is the copy that gets
/// made on their behalf.
/// </remarks>
public class Backup : BaseEntity<Guid>
{
    public Guid OwnerId { get; set; }

    /// <summary>
    /// The export, gzipped. Stored in the database rather than on disk because the deployment is
    /// Postgres and a web process — a file on a container's filesystem is gone at the next deploy,
    /// which is the one moment a backup has to survive.
    /// </summary>
    public required byte[] Content { get; set; }

    /// <summary>Compressed size, so a listing can say how big a download will be without reading it.</summary>
    public int SizeBytes { get; set; }

    /// <summary>SHA-256 of the uncompressed export, so an unchanged library is not stored twice.</summary>
    public required string ContentHash { get; set; }

    public int PlaylistCount { get; set; }

    public int ItemCount { get; set; }

    /// <summary>Whether the schedule made this one, or a person asked for it.</summary>
    public bool Automatic { get; set; }

    /// <summary>
    /// How many snapshots a user keeps. Enough to go back past a mistake noticed a few weeks
    /// later; few enough that the table is bounded by accounts rather than by time.
    /// </summary>
    public const int KeepPerUser = 5;

    /// <summary>How stale the newest snapshot has to be before another is worth making.</summary>
    public const int IntervalDays = 7;

    /// <summary>
    /// A ceiling on one snapshot. A library this large is a signal something is wrong — and a row
    /// this large is one Postgres has to TOAST and stream on every read.
    /// </summary>
    public const int MaxBytes = 32 * 1024 * 1024;
}
