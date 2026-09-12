using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Linkbelli.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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
    public DbSet<PlaylistPreference> PlaylistPreferences => Set<PlaylistPreference>();
    public DbSet<PlaylistItemTag> PlaylistItemTags => Set<PlaylistItemTag>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<AutomationRule> AutomationRules => Set<AutomationRule>();
    public DbSet<PlaylistLike> PlaylistLikes => Set<PlaylistLike>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<PlaylistMember> PlaylistMembers => Set<PlaylistMember>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<SourceTemplate> SourceTemplates => Set<SourceTemplate>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderPlaylist> FolderPlaylists => Set<FolderPlaylist>();
    public DbSet<Backup> Backups => Set<Backup>();

    /// <summary>
    /// The weighted vector every search matches against.
    /// </summary>
    /// <remarks>
    /// Every part is IMMUTABLE, which a generated column requires — hence the text search
    /// configuration named as a literal rather than left to default_text_search_config,
    /// which is a session setting and so is not.
    /// </remarks>
    private static readonly string SearchVectorSql = string.Join(" || ",
        Weighted("Title", 'A'),
        Weighted("SiteName", 'B'),
        Weighted("Description", 'C'),
        Weighted("Content", 'D'));

    /// <summary>The name the host+path is mapped under. A shadow property, like the vector.</summary>
    public const string HostPathProperty = "HostPath";

    /// <summary>
    /// The address with the scheme and query stripped and any trailing slash removed.
    /// </summary>
    /// <remarks>
    /// Matches what DuplicateService used to compute with Uri.Host + AbsolutePath. It can be
    /// this blunt because CanonicalUrl is already canonical — host lowercased, tracking
    /// parameters gone, query sorted — so there is nothing left for a real parser to fix.
    /// </remarks>
    private const string HostPathSql =
        @"rtrim(regexp_replace(regexp_replace(""CanonicalUrl"", '^https?://', ''), '\?.*$', ''), '/')";

    private static string Weighted(string column, char weight) =>
        $"setweight(to_tsvector('{PostgresFullTextSearch.Configuration}', coalesce(\"{column}\", '')), '{weight}')";

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

            // Search used LOWER(col) LIKE '%needle%' across seven columns, one of them up
            // to 60 000 characters of article text — no index could help, and every
            // candidate row had to be detoasted to answer it. Measured at 45ms over 38
            // links, on a Links table already 18MB for those 38 rows.
            //
            // Generated and stored, so the database maintains it on write and it cannot
            // drift from the row the way a trigger or an application-side update
            // eventually does. Weighted A-D so a title match outranks a mention in the
            // body, which is what the hand-rolled relevance buckets were reaching for.
            //
            // A shadow property: no entity carries it, because nothing outside the search
            // implementation has a use for it and Core does not depend on Npgsql.
            e.Property<NpgsqlTsVector>(PostgresFullTextSearch.VectorProperty)
                .HasComputedColumnSql(SearchVectorSql, stored: true);

            e.HasIndex(PostgresFullTextSearch.VectorProperty)
                .HasDatabaseName("IX_Links_SearchVector")
                .HasMethod("GIN");

            // The part of an address that names the page rather than how you reached it.
            // Stored so the duplicates view can group on it: it used to pull every item the
            // caller owns into memory, parse each URL with Uri, and group there — for a page
            // that most of the time renders "Nothing saved twice".
            e.Property<string>(HostPathProperty)
                .HasComputedColumnSql(HostPathSql, stored: true)
                .HasMaxLength(2048);

            e.HasIndex(HostPathProperty).HasDatabaseName("IX_Links_HostPath");

            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.Property(r => r.Key).HasMaxLength(200);
            e.Property(r => r.Endpoint).HasMaxLength(500);
            e.Property(r => r.RequestHash).HasMaxLength(64);
            // Per caller: two clients picking the same UUID must not collide. Unique, because it
            // is what makes two simultaneous retries resolve to one execution.
            e.HasIndex(r => new { r.UserId, r.Key }).IsUnique();
            e.HasIndex(r => r.CreationTime);
        });

        modelBuilder.Entity<Backup>(e =>
        {
            e.Property(b => b.ContentHash).HasMaxLength(64);
            // Newest first, per owner — every read of this table is "what does this person have".
            e.HasIndex(b => new { b.OwnerId, b.CreationTime });
        });

        modelBuilder.Entity<ContentReport>(e =>
        {
            e.Property(r => r.Note).HasMaxLength(1000);
            e.Property(r => r.Resolution).HasMaxLength(1000);
            e.HasIndex(r => new { r.Status, r.CreationTime });
            e.HasIndex(r => r.PlaylistId);
            e.HasOne(r => r.Playlist).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.Property(a => a.ActorName).HasMaxLength(256);
            e.Property(a => a.Action).HasMaxLength(100);
            e.Property(a => a.TargetType).HasMaxLength(50);
            e.Property(a => a.Summary).HasMaxLength(1000);
            e.Property(a => a.Details).HasColumnType("jsonb");
            e.HasIndex(a => a.CreationTime);
            e.HasIndex(a => new { a.Action, a.CreationTime });
            e.HasIndex(a => a.ActorId);
            e.HasIndex(a => a.TargetId);
            // Deliberately no soft-delete filter: an audit trail that can be deleted from the
            // application is not one.
        });

        modelBuilder.Entity<PlaylistMember>(e =>
        {
            e.HasIndex(m => new { m.PlaylistId, m.UserId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(m => m.UserId);
            e.HasOne(m => m.Playlist).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<Follow>(e =>
        {
            // One follow per target per person, for each kind of target.
            e.HasIndex(f => new { f.FollowerId, f.PlaylistId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(f => new { f.FollowerId, f.FollowedUserId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(f => f.PlaylistId);
            e.HasIndex(f => f.FollowedUserId);
            e.HasOne(f => f.Playlist).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistLike>(e =>
        {
            // One like per person per playlist — the count is meaningless otherwise.
            e.HasIndex(l => new { l.PlaylistId, l.UserId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(l => l.UserId);
            e.HasOne(l => l.Playlist).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<AutomationRule>(e =>
        {
            e.Property(r => r.Name).HasMaxLength(200);
            e.Property(r => r.Host).HasMaxLength(255);
            e.Property(r => r.TitlePattern).HasMaxLength(200);
            e.Property(r => r.UrlPattern).HasMaxLength(200);
            e.HasIndex(r => new { r.OwnerId, r.Position });
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistItem>(e =>
        {
            e.HasIndex(i => new { i.PlaylistId, i.LinkId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(i => new { i.PlaylistId, i.Position });
            // "What did I add to this shared list" — the question the column exists for.
            e.HasIndex(i => new { i.PlaylistId, i.AddedByUserId });
            e.Property(i => i.ShareToken).HasMaxLength(64);
            // The lookup a share link makes, and the uniqueness a token needs. Partial: almost
            // nothing is shared, and an index over every item would be mostly nulls.
            e.HasIndex(i => i.ShareToken)
                .IsUnique()
                .HasDatabaseName("IX_PlaylistItems_ShareToken")
                .HasFilter("\"ShareToken\" IS NOT NULL");
            // Feeds the automation sweep, which looks for items the rules haven't seen. Partial,
            // so it holds only the backlog rather than every item ever saved.
            e.HasIndex(i => i.CreationTime)
                .HasDatabaseName("IX_PlaylistItems_AwaitingAutomation")
                .HasFilter("\"AutomationAppliedAt\" IS NULL");
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
            e.Property(s => s.Filter).HasColumnType("jsonb");
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

        modelBuilder.Entity<SourceTemplate>(e =>
        {
            // Built-ins are matched by key so seeding updates rather than duplicates them.
            e.HasIndex(t => t.Key).IsUnique().ExcludeSoftDeleted();
            e.Property(t => t.Key).HasMaxLength(64);
            e.Property(t => t.Name).HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.SuggestedSchedule).HasMaxLength(100);
            e.Property(t => t.BaseConfig).HasColumnType("jsonb");
            e.Property(t => t.Fields).HasColumnType("jsonb");
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<SavedSearch>(e =>
        {
            e.HasIndex(ss => ss.OwnerId);
            e.Property(ss => ss.Name).HasMaxLength(200);
            e.Property(ss => ss.Query).HasMaxLength(200);
            e.Property(ss => ss.Host).HasMaxLength(255);
            e.Property(ss => ss.Status).HasMaxLength(16);
            e.Property(ss => ss.Sort).HasMaxLength(32);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistItemTag>(e =>
        {
            e.HasIndex(it => new { it.PlaylistItemId, it.TagId }).IsUnique().ExcludeSoftDeleted();
            e.HasIndex(it => it.TagId); // tag → items, for filtering a search by tag
            e.HasOne(it => it.PlaylistItem).WithMany(i => i.Tags).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(it => it.Tag).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasSoftDeleteFilter();
        });

        modelBuilder.Entity<PlaylistPreference>(e =>
        {
            // One row per person per playlist — read on every playlist open, so it's indexed
            // on exactly that pair.
            e.HasIndex(pp => new { pp.OwnerId, pp.PlaylistId }).IsUnique().ExcludeSoftDeleted();
            e.Property(pp => pp.Sort).HasMaxLength(32);
            e.Property(pp => pp.Source).HasMaxLength(64);
            e.Property(pp => pp.Status).HasMaxLength(16);
            e.HasOne(pp => pp.Playlist).WithMany().OnDelete(DeleteBehavior.Cascade);
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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var constraint))
        {
            throw new UniqueConstraintException(constraint, ex);
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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var constraint))
        {
            throw new UniqueConstraintException(constraint, ex);
        }
    }

    /// <summary>
    /// Recognises "somebody already has that value" and names the index that said so.
    /// </summary>
    /// <remarks>
    /// The only place in the application that reads a Postgres error code. Everything above gets
    /// <see cref="UniqueConstraintException"/> and a constraint name, so nothing else has to know
    /// which database is underneath.
    /// </remarks>
    private static bool IsUniqueViolation(DbUpdateException ex, out string? constraintName)
    {
        if (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            constraintName = pg.ConstraintName;
            return true;
        }

        constraintName = null;
        return false;
    }

    /// <summary>
    /// Stamps CreationTime on inserts, LastModified on anything that changed, and converts hard
    /// deletes into soft deletes. A soft delete counts as a change: a client syncing has to learn
    /// that a row went away, and the row is still there to tell it.
    /// </summary>
    private void ApplyAuditRules()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not BaseEntity<Guid> entity)
            {
                continue;
            }

            if (entry.State == EntityState.Added)
            {
                if (entity.CreationTime == default)
                {
                    entity.CreationTime = now;
                }

                entity.LastModified = now;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entity.DeletionTime = now;
                entity.LastModified = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entity.LastModified = now;
            }
        }
    }
}
