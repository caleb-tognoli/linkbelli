using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Linkbelli.Application.Data;

/// <summary>
/// The persistence surface the Application layer depends on, implemented by the EF
/// DbContext in Infrastructure. Keeps Application free of the Infrastructure project
/// while still using EF Core LINQ directly.
/// </summary>
public interface IAppDbContext
{
    DbSet<ApplicationUser> Users { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<Playlist> Playlists { get; }
    DbSet<Host> Hosts { get; }
    DbSet<Link> Links { get; }
    DbSet<PlaylistItem> PlaylistItems { get; }
    DbSet<Source> Sources { get; }
    DbSet<PlaylistSource> PlaylistSources { get; }
    DbSet<SourceRun> SourceRuns { get; }
    DbSet<UserQuota> UserQuotas { get; }
    DbSet<Tag> Tags { get; }
    DbSet<PlaylistTag> PlaylistTags { get; }
    DbSet<Folder> Folders { get; }
    DbSet<FolderPlaylist> FolderPlaylists { get; }

    /// <summary>Begins a database transaction, pinning a single connection for the duration.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the PostgreSQL random seed so that subsequent <c>ORDER BY random()</c> queries
    /// produce a deterministic order on the same connection.
    /// </summary>
    Task SeedRandomAsync(double seed, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry Entry(object entity);
}
