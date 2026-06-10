namespace Linkbelli.Core.Entities;

/// <summary>
/// Base for all persisted entities. CreationTime is stamped by the DbContext
/// on insert; update time is intentionally not tracked. Deletion is always
/// soft: DeletionTime is set instead of removing the row, and a global query
/// filter hides soft-deleted rows from all queries by default.
/// </summary>
public abstract class BaseEntity<TKey>
{
    public TKey Id { get; set; } = default!;
    public DateTimeOffset CreationTime { get; set; }
    public DateTimeOffset? DeletionTime { get; set; }

    public bool IsDeleted => DeletionTime is not null;
}
