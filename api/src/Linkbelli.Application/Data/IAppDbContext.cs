using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Linkbelli.Application.Data;

/// <summary>
/// The persistence surface the Application layer depends on, implemented by the EF
/// DbContext in Infrastructure.
/// </summary>
/// <remarks>
/// What this buys is narrower than it looks, and it is worth being plain about it.
///
/// It keeps the Application project from referencing Infrastructure: Npgsql's types, the
/// migrations, Identity's store configuration, Hangfire and the SMTP client stay out of reach,
/// so a service cannot quietly start depending on one of them. Anything that has to be
/// Postgres-specific goes behind its own narrow interface instead — <c>IFullTextSearch</c> for
/// text search, <c>ISeededShuffle</c> for a repeatable random order — and not on this one.
///
/// What it does not buy is a persistence-agnostic Application layer. The services are written
/// against EF Core LINQ and depend on how EF translates it; several say so in comments. Nor does
/// it make them unit-testable: it hands out real <c>DbSet</c>s, which cannot sensibly be faked,
/// which is why the services are tested against a real Postgres in the integration suite. Going
/// further in either direction — dropping this and referencing Infrastructure, or real
/// repositories with domain-shaped methods — was considered and not judged worth it yet.
/// </remarks>
public interface IAppDbContext
{
    DbSet<ApplicationUser> Users { get; }

    /// <summary>Identity's role tables, for the one question the app asks of them: is this an admin.</summary>
    DbSet<IdentityUserRole<Guid>> UserRoles { get; }
    DbSet<IdentityRole<Guid>> Roles { get; }
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
    DbSet<PlaylistPreference> PlaylistPreferences { get; }
    DbSet<PlaylistItemTag> PlaylistItemTags { get; }
    DbSet<SavedSearch> SavedSearches { get; }
    DbSet<AutomationRule> AutomationRules { get; }
    DbSet<PlaylistLike> PlaylistLikes { get; }
    DbSet<Follow> Follows { get; }
    DbSet<PlaylistMember> PlaylistMembers { get; }
    DbSet<Invite> Invites { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<ContentReport> ContentReports { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<SourceTemplate> SourceTemplates { get; }
    DbSet<Folder> Folders { get; }
    DbSet<FolderPlaylist> FolderPlaylists { get; }
    DbSet<Backup> Backups { get; }
    DbSet<Highlight> Highlights { get; }
    DbSet<Webhook> Webhooks { get; }
    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry Entry(object entity);
}
