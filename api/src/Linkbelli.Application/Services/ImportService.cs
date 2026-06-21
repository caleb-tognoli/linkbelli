using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Playlists;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class ImportService(
    IAppDbContext db,
    IPlaylistService playlists,
    ILinkEnrichmentQueue enrichmentQueue) : IImportService
{
    public const int MaxRows = 2000;

    public async Task<ImportResult> ImportAsync(Guid ownerId, ImportRequest request, CancellationToken ct = default)
    {
        if (request.Rows.Length == 0)
            return new ImportResult(0, 0, []);

        if (request.Rows.Length > MaxRows)
            throw new ValidationException("rows",
                $"CSV imports are limited to {MaxRows} rows. Split your file and import in batches.");

        Guid? playlistId = request.PlaylistId;

        if (!string.IsNullOrWhiteSpace(request.NewPlaylistName))
        {
            var created = await playlists.CreateAsync(ownerId,
                new CreatePlaylistRequest(request.NewPlaylistName.Trim(), null, null, null), ct);
            playlistId = created.Id;
        }

        if (playlistId is not null &&
            !await db.Playlists.AnyAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct))
        {
            throw new NotFoundException("Playlist not found.");
        }

        var errors = new List<string>();

        // --- Step 1: canonicalize all rows upfront ---
        var valid = new List<(ImportRow Row, CanonicalUrl Canonical)>(request.Rows.Length);
        foreach (var row in request.Rows)
        {
            if (UrlCanonicalizer.TryCanonicalize(row.Url, out var canonical))
                valid.Add((row, canonical));
            else
                errors.Add($"Invalid URL: {row.Url}");
        }

        if (valid.Count == 0)
            return new ImportResult(0, 0, errors);

        // --- Step 2: batch-load links that already exist in the DB ---
        var allHashes = valid.Select(v => v.Canonical.Hash).Distinct().ToList();
        var existingLinks = await db.Links
            .Include(l => l.Host)
            .Where(l => allHashes.Contains(l.UrlHash))
            .ToDictionaryAsync(l => l.UrlHash, ct);

        // Flag blocked hosts found in existing links
        var blockedHosts = existingLinks.Values
            .Where(l => l.Host!.Blocked)
            .Select(l => l.Host!.Hostname)
            .ToHashSet();

        // --- Step 3: batch-load hosts needed for brand-new URLs ---
        var newHashes = allHashes.Where(h => !existingLinks.ContainsKey(h)).ToList();
        var newHostnames = valid
            .Where(v => newHashes.Contains(v.Canonical.Hash))
            .Select(v => v.Canonical.Host)
            .Distinct()
            .ToList();

        var knownHosts = await db.Hosts
            .Where(h => newHostnames.Contains(h.Hostname))
            .ToDictionaryAsync(h => h.Hostname, ct);

        foreach (var h in knownHosts.Values.Where(h => h.Blocked))
            blockedHosts.Add(h.Hostname);

        // Create any missing host rows (usually very few distinct hostnames)
        var createdHosts = new Dictionary<string, Host>();
        foreach (var hostname in newHostnames.Where(h => !knownHosts.ContainsKey(h)))
        {
            var host = new Host { Hostname = hostname };
            db.Hosts.Add(host);
            createdHosts[hostname] = host;
        }

        // --- Step 4: create link rows for every new URL ---
        // Track within-file duplicates (same canonical URL appearing multiple times).
        var seenHashes = new HashSet<string>(existingLinks.Keys);
        var newLinks = new List<Link>();

        foreach (var (row, canonical) in valid)
        {
            if (!seenHashes.Add(canonical.Hash))
                continue; // duplicate within this import batch — already handled

            if (existingLinks.ContainsKey(canonical.Hash))
                continue; // already in DB

            if (blockedHosts.Contains(canonical.Host))
            {
                errors.Add($"{row.Url}: Links from '{canonical.Host}' are blocked.");
                continue;
            }

            var host = createdHosts.TryGetValue(canonical.Host, out var ch) ? ch
                : knownHosts.GetValueOrDefault(canonical.Host);
            if (host is null) continue; // shouldn't happen

            var link = new Link
            {
                CanonicalUrl = canonical.Url,
                UrlHash = canonical.Hash,
                Host = host,
            };
            db.Links.Add(link);
            newLinks.Add(link);
            existingLinks[canonical.Hash] = link;
        }

        // --- Step 5: build playlist items ---
        int imported = 0, skipped = 0;

        HashSet<Guid>? existingItemLinkIds = null;
        long maxPos = 0;
        if (playlistId is not null)
        {
            existingItemLinkIds = (await db.PlaylistItems
                .Where(i => i.PlaylistId == playlistId)
                .Select(i => i.LinkId)
                .ToListAsync(ct)).ToHashSet();

            maxPos = await db.PlaylistItems
                .Where(i => i.PlaylistId == playlistId)
                .MaxAsync(i => (long?)i.Position, ct) ?? 0;
        }

        // Track within-batch link IDs to avoid double-adding duplicates to the playlist
        var addedLinkIds = new HashSet<Guid>();

        foreach (var (row, canonical) in valid)
        {
            if (!existingLinks.TryGetValue(canonical.Hash, out var link))
                continue; // blocked or invalid — already in errors

            if (link.Host!.Blocked)
                continue;

            if (playlistId is null)
            {
                imported++;
                continue;
            }

            if (!addedLinkIds.Add(link.Id))
                continue; // within-file duplicate for this playlist

            if (existingItemLinkIds!.Contains(link.Id))
            {
                skipped++;
                continue;
            }

            maxPos += PlaylistOrdering.Gap;
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlistId.Value,
                LinkId = link.Id,
                Position = maxPos,
                Note = row.Note?.Trim(),
                Status = PlaylistItemStatus.Added,
            });
            existingItemLinkIds.Add(link.Id);
            imported++;
        }

        // --- Step 6: persist hosts + links + items in one round-trip ---
        await db.SaveChangesAsync(ct);

        // --- Step 7: enqueue enrichment for every brand-new link ---
        foreach (var link in newLinks)
            enrichmentQueue.Enqueue(link.Id);

        return new ImportResult(imported, skipped, errors);
    }
}
