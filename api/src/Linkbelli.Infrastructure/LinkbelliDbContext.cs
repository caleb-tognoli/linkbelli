using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Infrastructure;

public class LinkbelliDbContext(DbContextOptions<LinkbelliDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<Host> Hosts => Set<Host>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<PlaylistSource> PlaylistSources => Set<PlaylistSource>();
    public DbSet<SourceRun> SourceRuns => Set<SourceRun>();
    public DbSet<UserQuota> UserQuotas => Set<UserQuota>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<PlaylistTag> PlaylistTags => Set<PlaylistTag>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderPlaylist> FolderPlaylists => Set<FolderPlaylist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity tables

        // Soft-delete conventions are applied via HasSoftDeleteFilter() (query filter)
        // and ExcludeSoftDeleted() (partial unique index) — see SoftDeleteModelExtensions.
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.Property(k => k.Name).HasMaxLength(200);
            e.Property(k => k.Prefix).HasMaxLength(64);
            e.Property(k => k.Hash).HasMaxLength(64);
            e.HasIndex(k => k.Prefix).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(k => k.UserId);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Description).HasMaxLength(2000);
            e.HasIndex(p => new { p.OwnerId, p.Slug }).IsUnique().ExcludeSoftDeleted();
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Host>(e =>
        {
            e.Property(h => h.Hostname).HasMaxLength(253); // DNS hostname limit
            e.HasIndex(h => h.Hostname).IsUnique().ExcludeSoftDeleted();
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Link>(e =>
        {
            e.Property(l => l.CanonicalUrl).HasMaxLength(2048);
            e.Property(l => l.UrlHash).HasMaxLength(64);
            e.Property(l => l.Metadata).HasColumnType("jsonb");
            e.HasIndex(l => l.UrlHash).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(l => l.HostId);
            e.HasOne(l => l.Host).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistItem>(e =>
        {
            e.HasIndex(i => new { i.PlaylistId, i.LinkId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(i => new { i.PlaylistId, i.Position });
            e.HasOne(i => i.Playlist).WithMany(p => p.Items).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Link).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Source).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.Property(i => i.Metadata).HasColumnType("jsonb");
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Source>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.Schedule).HasMaxLength(100);
            e.Property(s => s.Config).HasColumnType("jsonb");
            e.Property(s => s.State).HasColumnType("jsonb");
            e.HasIndex(s => s.OwnerId);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistSource>(e =>
        {
            e.HasIndex(ps => new { ps.PlaylistId, ps.SourceId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(ps => ps.SourceId);
            e.HasOne(ps => ps.Playlist).WithMany(p => p.Sources).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ps => ps.Source).WithMany(s => s.Playlists).OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<SourceRun>(e =>
        {
            e.HasIndex(r => new { r.SourceId, r.CreationTime });
            e.HasOne(r => r.Source).WithMany(s => s.Runs).OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<UserQuota>(e =>
        {
            e.HasIndex(q => q.UserId).IsUnique().ExcludeSoftDeleted();
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(64);
            e.HasIndex(t => t.Name).IsUnique().ExcludeSoftDeleted();
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistTag>(e =>
        {
            e.HasIndex(pt => new { pt.PlaylistId, pt.TagId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(pt => pt.TagId); // tag → playlists (global search, counts)
            e.HasOne(pt => pt.Playlist).WithMany(p => p.Tags).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(pt => pt.Tag).WithMany(t => t.Playlists).OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Folder>(e =>
        {
            e.Property(f => f.Name).HasMaxLength(200);
            e.HasIndex(f => f.OwnerId);
            e.HasIndex(f => f.ParentId);
            // Self-reference for nesting. Soft delete intercepts hard deletes, so subfolders are
            // cascaded manually in FolderService; keep the FK Restrict to avoid surprise cascades.
            e.HasOne(f => f.Parent).WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<FolderPlaylist>(e =>
        {
            // A playlist is filed in at most one folder per owner.
            e.HasIndex(fp => new { fp.OwnerId, fp.PlaylistId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(fp => fp.FolderId);
            e.HasOne(fp => fp.Folder).WithMany(f => f.Playlists)
                .HasForeignKey(fp => fp.FolderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(fp => fp.Playlist).WithMany()
                .HasForeignKey(fp => fp.PlaylistId).OnDelete(DeleteBehavior.Restrict);
            e.HasSoftDeleteFilter();
        });

        // Optimistic concurrency for every domain entity via Postgres's xmin system
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property<uint>("xmin")
                    .HasColumnName("xmin")
                    .HasColumnType("xmin")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
        }
    }

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    public Task SeedRandomAsync(double seed, CancellationToken cancellationToken = default)
    {
        // seed is always a double formatted with "R" — no SQL injection risk.
        var literal = seed.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
#pragma warning disable EF1002
        return Database.ExecuteSqlRawAsync($"SELECT setseed({literal})", cancellationToken);
#pragma warning restore EF1002
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The resource was modified concurrently. Reload and try again.");
        }
    }

    public override int SaveChanges()
    {
        ApplyAuditRules();
        try
        {
            return base.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The resource was modified concurrently. Reload and try again.");
        }
    }

    /// <summary>Stamps CreationTime on inserts and converts hard deletes into soft deletes.</summary>
    private void ApplyAuditRules()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not BaseEntity<Guid> entity)
            {
                continue;
            }

            if (entry.State == EntityState.Added && entity.CreationTime == default)
            {
                entity.CreationTime = now;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entity.DeletionTime = now;
            }
        }
    }
}
