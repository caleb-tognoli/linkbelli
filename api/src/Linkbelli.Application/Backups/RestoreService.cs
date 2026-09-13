using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Export;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Tags;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Backups;

/// <summary>
/// Putting a backup back.
/// </summary>
/// <remarks>
/// A backup system with no restore is a file-copying system. Everything else was here — weekly
/// snapshots, five-deep retention, content hashing so an unchanged library is not stored twice —
/// and the one thing the whole apparatus exists for, getting your library back on the day
/// something goes wrong, was missing. The app said so in its own UI, which was honest and did not
/// make it any less missing.
///
/// **Merge, not replace.** A restore adds what is absent and leaves alone what is there. The
/// alternative — wipe and rewrite — is a far more destructive operation than most people asking
/// for a restore have in mind, and it would throw away everything saved since the snapshot. So:
///
/// - Playlists are matched by slug, folders by name and parent, sources by name.
/// - Items are matched by canonical URL within their playlist. Links deduplicate globally, so a
///   restored item points at the row that already exists rather than making another.
/// - An item that is already there keeps its own note, score and status. Restoring a snapshot
///   should not undo the reading you have done since.
///
/// **A dry run first**, because the only way to trust this is to be told what it will do while it
/// is still possible to decide otherwise.
/// </remarks>
public interface IRestoreService
{
    /// <summary>What restoring this snapshot would add, without adding it.</summary>
    Task<RestorePlan> PreviewAsync(Guid ownerId, Guid backupId, CancellationToken ct = default);

    /// <summary>Does it, and reports what it did.</summary>
    Task<RestorePlan> RestoreAsync(Guid ownerId, Guid backupId, CancellationToken ct = default);

    /// <summary>
    /// The same, from a file rather than a stored snapshot.
    /// </summary>
    /// <remarks>
    /// The case a backup system is actually for: the server it was taken from is gone, and what
    /// somebody has is the JSON they downloaded before it went.
    /// </remarks>
    Task<RestorePlan> RestoreFromAsync(
        Guid ownerId, string json, bool dryRun, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class RestoreService(
    IAppDbContext db,
    IBackupService backups,
    ILinkService links,
    ITagResolver tags) : IRestoreService
{
    /// <summary>
    /// The most items one restore will put back.
    /// </summary>
    /// <remarks>
    /// A restore writes a row per item on one request. This is well above any library a person
    /// curates and below the point where one request becomes everybody else's problem; past it
    /// the restore stops and says how much it did, rather than timing out halfway with no
    /// account of itself.
    /// </remarks>
    public const int MaxItems = 5_000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RestorePlan> PreviewAsync(Guid ownerId, Guid backupId, CancellationToken ct = default)
    {
        var (json, _) = await backups.DownloadAsync(ownerId, backupId, ct);
        return await ApplyAsync(ownerId, Parse(json), dryRun: true, ct);
    }

    public async Task<RestorePlan> RestoreAsync(Guid ownerId, Guid backupId, CancellationToken ct = default)
    {
        var (json, _) = await backups.DownloadAsync(ownerId, backupId, ct);
        return await ApplyAsync(ownerId, Parse(json), dryRun: false, ct);
    }

    public async Task<RestorePlan> RestoreFromAsync(
        Guid ownerId, string json, bool dryRun, CancellationToken ct = default) =>
        await ApplyAsync(ownerId, Parse(json), dryRun, ct);

    private static ExportBundle Parse(string json)
    {
        ExportBundle? bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ExportBundle>(json, Json);
        }
        catch (JsonException)
        {
            throw new ValidationException("file", "That does not look like a Linkbelli backup.");
        }

        if (bundle is null || bundle.Playlists is null)
        {
            throw new ValidationException("file", "That does not look like a Linkbelli backup.");
        }

        if (bundle.Version > ExportFormatVersion.Current)
        {
            // Written by a newer Linkbelli. Refused rather than half-read: a restore that
            // silently drops fields it does not understand is worse than one that declines.
            throw new ValidationException(
                "file",
                "That backup was made by a newer version of Linkbelli than this one. Update first.");
        }

        return bundle;
    }

    private async Task<RestorePlan> ApplyAsync(
        Guid ownerId, ExportBundle bundle, bool dryRun, CancellationToken ct)
    {
        var plan = new RestoreCounts();

        var folders = await RestoreFoldersAsync(ownerId, bundle, dryRun, plan, ct);
        await RestorePlaylistsAsync(ownerId, bundle, folders, dryRun, plan, ct);
        await RestoreSourcesAsync(ownerId, bundle, dryRun, plan, ct);

        if (!dryRun)
        {
            await db.SaveChangesAsync(ct);
        }

        return new RestorePlan(
            dryRun,
            bundle.Version,
            bundle.ExportedAt,
            plan.FoldersAdded,
            plan.PlaylistsAdded,
            plan.PlaylistsMatched,
            plan.ItemsAdded,
            plan.ItemsAlreadyThere,
            plan.SourcesAdded,
            plan.Truncated,
            // Config secrets are redacted on the way out — an export is a file that gets emailed
            // around, and a scraper's auth header has no business travelling in one. Which means
            // a restored source may need its credentials typed in again, and saying so here is
            // the difference between a surprise and an expectation.
            plan.SourcesAdded > 0);
    }

    private async Task<Dictionary<Guid, Guid>> RestoreFoldersAsync(
        Guid ownerId, ExportBundle bundle, bool dryRun, RestoreCounts plan, CancellationToken ct)
    {
        // Old id to new id, so a playlist's folder reference still points somewhere after the
        // folders have been recreated under different ids.
        var mapped = new Dictionary<Guid, Guid>();

        var existing = await db.Folders
            .Where(f => f.OwnerId == ownerId)
            .Select(f => new { f.Id, f.Name, f.ParentId })
            .ToListAsync(ct);

        // Parents before children, so a nested folder finds the parent it belongs under. The
        // export orders by name, which says nothing about depth.
        foreach (var folder in Ordered(bundle.Folders ?? []))
        {
            Guid? parentId = folder.ParentId is { } oldParent && mapped.TryGetValue(oldParent, out var newParent)
                ? newParent
                : null;

            var match = existing.FirstOrDefault(f =>
                string.Equals(f.Name, folder.Name, StringComparison.OrdinalIgnoreCase)
                && f.ParentId == parentId);

            if (match is not null)
            {
                mapped[folder.Id] = match.Id;
                continue;
            }

            plan.FoldersAdded++;
            if (dryRun)
            {
                // Still mapped, so the preview's playlist counts are the ones the real run will
                // produce rather than a different set based on folders that "do not exist".
                mapped[folder.Id] = folder.Id;
                continue;
            }

            var created = new Folder { OwnerId = ownerId, Name = folder.Name, ParentId = parentId };
            db.Folders.Add(created);
            mapped[folder.Id] = created.Id;
        }

        return mapped;
    }

    /// <summary>Folders with their parents first, and a cycle broken rather than looped on.</summary>
    private static IEnumerable<ExportFolder> Ordered(IReadOnlyList<ExportFolder> folders)
    {
        var remaining = folders.ToList();
        var placed = new HashSet<Guid>();

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(f => f.ParentId is null || placed.Contains(f.ParentId.Value))
                .ToList();

            if (ready.Count == 0)
            {
                // A parent that is not in the file, or a cycle. Either way the rest go in at the
                // top rather than being dropped: a folder in the wrong place is recoverable and
                // a folder that vanished is not.
                foreach (var orphan in remaining)
                {
                    yield return orphan with { ParentId = null };
                }

                yield break;
            }

            foreach (var folder in ready)
            {
                placed.Add(folder.Id);
                remaining.Remove(folder);
                yield return folder;
            }
        }
    }

    private async Task RestorePlaylistsAsync(
        Guid ownerId,
        ExportBundle bundle,
        Dictionary<Guid, Guid> folders,
        bool dryRun,
        RestoreCounts plan,
        CancellationToken ct)
    {
        var existing = await db.Playlists
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new { p.Id, p.Slug })
            .ToListAsync(ct);

        foreach (var playlist in bundle.Playlists)
        {
            if (plan.ItemsAdded >= MaxItems)
            {
                plan.Truncated = true;
                return;
            }

            var match = existing.FirstOrDefault(p =>
                string.Equals(p.Slug, playlist.Slug, StringComparison.OrdinalIgnoreCase));

            Guid playlistId;
            if (match is not null)
            {
                plan.PlaylistsMatched++;
                playlistId = match.Id;
            }
            else
            {
                plan.PlaylistsAdded++;
                playlistId = dryRun ? playlist.Id : await CreatePlaylistAsync(ownerId, playlist, folders, ct);
            }

            await RestoreItemsAsync(ownerId, playlistId, playlist, match is not null, dryRun, plan, ct);
        }
    }

    private async Task<Guid> CreatePlaylistAsync(
        Guid ownerId, ExportPlaylist playlist, Dictionary<Guid, Guid> folders, CancellationToken ct)
    {
        var created = new Playlist
        {
            OwnerId = ownerId,
            Name = playlist.Name,
            Slug = playlist.Slug,
            Description = playlist.Description,
            // Private whatever it was. Republishing somebody's lists as a side effect of a
            // restore is the kind of surprise that makes people stop trusting a button.
            Visibility = PlaylistVisibility.Private,
        };

        db.Playlists.Add(created);

        foreach (var tag in await tags.ResolveAsync(TagNormalizer.Normalize(playlist.Tags ?? []), ct))
        {
            db.PlaylistTags.Add(new PlaylistTag { PlaylistId = created.Id, TagId = tag.Id });
        }

        if (playlist.FolderId is { } oldFolder && folders.TryGetValue(oldFolder, out var folderId))
        {
            db.FolderPlaylists.Add(new FolderPlaylist
            {
                OwnerId = ownerId,
                PlaylistId = created.Id,
                FolderId = folderId,
            });
        }

        return created.Id;
    }

    private async Task RestoreItemsAsync(
        Guid ownerId,
        Guid playlistId,
        ExportPlaylist playlist,
        bool playlistExisted,
        bool dryRun,
        RestoreCounts plan,
        CancellationToken ct)
    {
        // What is already in this playlist, by canonical URL. Only worth asking when the
        // playlist was already here — a new one holds nothing.
        var present = playlistExisted
            ? (await db.PlaylistItems
                .Where(i => i.PlaylistId == playlistId)
                .Select(i => i.Link!.UrlHash)
                .ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        var position = playlistExisted && !dryRun
            ? await db.PlaylistItems.Where(i => i.PlaylistId == playlistId)
                .MaxAsync(i => (long?)i.Position, ct) ?? 0
            : 0;

        foreach (var item in playlist.Items ?? [])
        {
            if (plan.ItemsAdded >= MaxItems)
            {
                plan.Truncated = true;
                return;
            }

            if (!UrlCanonicalizer.TryCanonicalize(item.Url, out var canonical))
            {
                continue;
            }

            if (!present.Add(canonical.Hash))
            {
                // Already here. Left exactly as it is: a restore should not undo the reading
                // somebody has done since the snapshot was taken.
                plan.ItemsAlreadyThere++;
                continue;
            }

            plan.ItemsAdded++;
            if (dryRun)
            {
                continue;
            }

            // Links deduplicate globally, so this points at the row that already exists whenever
            // anybody has saved the same page — a restore of five hundred links writes five
            // hundred item rows and, usually, far fewer link rows.
            var link = await links.GetOrCreateAsync(canonical, initialTitle: item.Title, cancellationToken: ct);

            position += PlaylistOrdering.Gap;
            var restored = new PlaylistItem
            {
                PlaylistId = playlistId,
                LinkId = link.Id,
                Position = position,
                Note = item.Note,
                Score = item.Score,
                Status = Enum.TryParse<PlaylistItemStatus>(item.Status, ignoreCase: true, out var status)
                    ? status
                    : PlaylistItemStatus.Added,
                Metadata = item.Metadata is null ? null : new Dictionary<string, string>(item.Metadata),
                AddedByUserId = ownerId,
            };

            db.PlaylistItems.Add(restored);

            foreach (var tag in await tags.ResolveAsync(TagNormalizer.Normalize(item.Tags ?? []), ct))
            {
                db.PlaylistItemTags.Add(new PlaylistItemTag
                {
                    PlaylistItemId = restored.Id,
                    TagId = tag.Id,
                });
            }
        }
    }

    private async Task RestoreSourcesAsync(
        Guid ownerId, ExportBundle bundle, bool dryRun, RestoreCounts plan, CancellationToken ct)
    {
        var existing = await db.Sources
            .Where(s => s.OwnerId == ownerId)
            .Select(s => s.Name)
            .ToListAsync(ct);

        foreach (var source in bundle.Sources ?? [])
        {
            if (existing.Any(name => string.Equals(name, source.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!Enum.TryParse<SourceType>(source.Type, ignoreCase: true, out var type))
            {
                continue;
            }

            plan.SourcesAdded++;
            if (dryRun)
            {
                continue;
            }

            db.Sources.Add(new Source
            {
                OwnerId = ownerId,
                Name = source.Name,
                Type = type,
                Config = JsonSerializer.Serialize(source.Config ?? new Dictionary<string, string>()),
                Schedule = source.Schedule,
                // Paused. The config came out of the export with its secrets redacted, so a
                // restored source may be missing credentials — and one that starts fetching on a
                // schedule with half a config just fails on a timer until somebody notices.
                Status = SourceStatus.Paused,
                Visibility = SourceVisibility.Private,
            });
        }
    }

    /// <summary>Running totals, so the dry run and the real thing count the same way.</summary>
    private sealed class RestoreCounts
    {
        public int FoldersAdded { get; set; }

        public int PlaylistsAdded { get; set; }

        public int PlaylistsMatched { get; set; }

        public int ItemsAdded { get; set; }

        public int ItemsAlreadyThere { get; set; }

        public int SourcesAdded { get; set; }

        public bool Truncated { get; set; }
    }
}
