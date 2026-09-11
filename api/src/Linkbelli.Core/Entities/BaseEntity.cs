namespace Linkbelli.Core.Entities;

/// <summary>Anything that is soft-deleted by stamping a DeletionTime rather than removing the row.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletionTime { get; set; }
}

/// <summary>
/// Base for all persisted entities. CreationTime and LastModified are stamped by the DbContext.
/// Deletion is always soft: DeletionTime is set instead of removing the row, and a global query
/// filter hides soft-deleted rows from all queries by default.
/// </summary>
public abstract class BaseEntity<TKey> : ISoftDeletable
{
    public TKey Id { get; set; } = default!;
    public DateTimeOffset CreationTime { get; set; }

    /// <summary>
    /// When this row last changed, including the change that soft-deleted it. Without it a
    /// caching client — an extension, an offline queue, a mobile app — has no way to ask what
    /// happened since it last looked, and has to re-read everything or nothing.
    /// </summary>
    public DateTimeOffset LastModified { get; set; }

    public DateTimeOffset? DeletionTime { get; set; }

    public bool IsDeleted => DeletionTime is not null;
}
