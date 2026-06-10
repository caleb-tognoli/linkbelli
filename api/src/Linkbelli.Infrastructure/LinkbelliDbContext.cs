using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Infrastructure;

public class LinkbelliDbContext(DbContextOptions<LinkbelliDbContext> options) : DbContext(options)
{
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<Host> Hosts => Set<Host>();
    public DbSet<Link> Links => Set<Link>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<PlaylistSource> PlaylistSources => Set<PlaylistSource>();
    public DbSet<SourceRun> SourceRuns => Set<SourceRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Unique indexes are partial ("DeletionTime" IS NULL) so a soft-deleted
        // row never blocks re-creating the same playlist slug, link, etc.
        modelBuilder.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Slug).HasMaxLength(200);
            e.Property(p => p.Description).HasMaxLength(2000);
            e.HasIndex(p => new { p.OwnerId, p.Slug }).IsUnique().HasFilter("\"DeletionTime\" IS NULL");
            e.HasQueryFilter(p => p.DeletionTime == null);
        });

        modelBuilder.Entity<Host>(e =>
        {
            e.Property(h => h.Hostname).HasMaxLength(253); // DNS hostname limit
            e.HasIndex(h => h.Hostname).IsUnique().HasFilter("\"DeletionTime\" IS NULL");
            e.HasQueryFilter(h => h.DeletionTime == null);
        });

        modelBuilder.Entity<Link>(e =>
        {
            e.Property(l => l.CanonicalUrl).HasMaxLength(2048);
            e.Property(l => l.UrlHash).HasMaxLength(64);
            e.Property(l => l.Metadata).HasColumnType("jsonb");
            e.HasIndex(l => l.UrlHash).IsUnique().HasFilter("\"DeletionTime\" IS NULL");
            e.HasIndex(l => l.HostId);
            e.HasOne(l => l.Host).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(l => l.DeletionTime == null);
        });

        modelBuilder.Entity<PlaylistItem>(e =>
        {
            e.HasIndex(i => new { i.PlaylistId, i.LinkId }).IsUnique().HasFilter("\"DeletionTime\" IS NULL");
            e.HasIndex(i => new { i.PlaylistId, i.Position });
            e.HasOne(i => i.Playlist).WithMany(p => p.Items).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Link).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Source).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(i => i.DeletionTime == null);
        });

        modelBuilder.Entity<Source>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(200);
            e.Property(s => s.Schedule).HasMaxLength(100);
            e.Property(s => s.Config).HasColumnType("jsonb");
            e.Property(s => s.State).HasColumnType("jsonb");
            e.HasIndex(s => s.OwnerId);
            e.HasQueryFilter(s => s.DeletionTime == null);
        });

        modelBuilder.Entity<PlaylistSource>(e =>
        {
            e.HasIndex(ps => new { ps.PlaylistId, ps.SourceId }).IsUnique().HasFilter("\"DeletionTime\" IS NULL");
            e.HasIndex(ps => ps.SourceId);
            e.HasOne(ps => ps.Playlist).WithMany(p => p.Sources).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ps => ps.Source).WithMany(s => s.Playlists).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(ps => ps.DeletionTime == null);
        });

        modelBuilder.Entity<SourceRun>(e =>
        {
            e.HasIndex(r => new { r.SourceId, r.CreationTime });
            e.HasOne(r => r.Source).WithMany(s => s.Runs).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(r => r.DeletionTime == null);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditRules();
        return base.SaveChanges();
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
