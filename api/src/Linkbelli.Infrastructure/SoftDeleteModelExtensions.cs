using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Linkbelli.Infrastructure;

/// <summary>
/// Centralizes the two soft-delete conventions so the literal column filter and
/// query filter live in one place instead of being repeated per entity.
/// </summary>
public static class SoftDeleteModelExtensions
{
    private const string NotDeletedSql = "\"DeletionTime\" IS NULL";

    /// <summary>Hides soft-deleted rows from all queries on this entity (opt out with IgnoreQueryFilters).</summary>
    public static EntityTypeBuilder<TEntity> HasSoftDeleteFilter<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ISoftDeletable
    {
        builder.HasQueryFilter(e => e.DeletionTime == null);
        return builder;
    }

    /// <summary>
    /// Makes an index partial so it only covers live rows — required on unique
    /// indexes so a soft-deleted row never blocks recreating the same key.
    /// </summary>
    public static IndexBuilder<TEntity> ExcludeSoftDeleted<TEntity>(this IndexBuilder<TEntity> builder)
        => builder.HasFilter(NotDeletedSql);
}
