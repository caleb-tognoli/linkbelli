using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Export;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Backups;

/// <summary>
/// Keeps snapshots of a user's library, and hands one back on request.
/// </summary>
/// <remarks>
/// The difference from <see cref="IExportService"/> is who remembers. An export happens because
/// somebody thought to ask for one; a backup happens whether or not they did.
/// </remarks>
public interface IBackupService
{
    /// <summary>The snapshots a user has, newest first. Metadata only — never the bytes.</summary>
    Task<IReadOnlyList<BackupResponse>> ListAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>Takes a snapshot now. Returns null when the library is unchanged since the last one.</summary>
    Task<BackupResponse?> CreateAsync(Guid ownerId, bool automatic = false, CancellationToken ct = default);

    /// <summary>The export inside one snapshot, decompressed.</summary>
    Task<(string Json, DateTimeOffset TakenAt)> DownloadAsync(
        Guid ownerId, Guid backupId, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid backupId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class BackupService(
    IAppDbContext db,
    IExportService exports,
    ILogger<BackupService> logger) : IBackupService
{
    public async Task<IReadOnlyList<BackupResponse>> ListAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.Backups
            .Where(b => b.OwnerId == ownerId)
            .OrderByDescending(b => b.CreationTime)
            .Select(b => new BackupResponse(
                b.Id, b.CreationTime, b.SizeBytes, b.PlaylistCount, b.ItemCount, b.Automatic))
            .ToListAsync(ct);

    public async Task<BackupResponse?> CreateAsync(
        Guid ownerId, bool automatic = false, CancellationToken ct = default)
    {
        var bundle = await exports.ExportAllAsync(ownerId, ct);

        // Serialized without the timestamp mattering: the hash has to answer "has the library
        // changed", and ExportedAt changes on every run by definition.
        var json = ExportSerializer.Serialize(bundle with { ExportedAt = default }, ExportFormat.Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        var newest = await db.Backups
            .Where(b => b.OwnerId == ownerId)
            .OrderByDescending(b => b.CreationTime)
            .Select(b => b.ContentHash)
            .FirstOrDefaultAsync(ct);

        if (newest == hash)
        {
            // Nothing has changed, so the snapshot that exists is still the right one. Storing a
            // second identical copy would only push an older, different one out of retention.
            return null;
        }

        // Stamped with the real time, not the zeroed one the hash was taken over.
        var stamped = ExportSerializer.Serialize(bundle, ExportFormat.Json);
        var content = Compress(stamped);

        if (content.Length > Backup.MaxBytes)
        {
            throw new ValidationException(
                "backup",
                $"This library is too large to snapshot ({content.Length / (1024 * 1024)} MB compressed). Export it by hand instead.");
        }

        var backup = new Backup
        {
            OwnerId = ownerId,
            Content = content,
            SizeBytes = content.Length,
            ContentHash = hash,
            PlaylistCount = bundle.Playlists.Count,
            ItemCount = bundle.Playlists.Sum(p => p.Items.Count),
            Automatic = automatic,
        };

        db.Backups.Add(backup);
        await db.SaveChangesAsync(ct);
        await TrimAsync(ownerId, ct);

        logger.LogInformation(
            "Backed up {Items} items across {Playlists} playlists for {Owner} ({Size} bytes).",
            backup.ItemCount, backup.PlaylistCount, ownerId, backup.SizeBytes);

        return new BackupResponse(
            backup.Id, backup.CreationTime, backup.SizeBytes,
            backup.PlaylistCount, backup.ItemCount, backup.Automatic);
    }

    public async Task<(string Json, DateTimeOffset TakenAt)> DownloadAsync(
        Guid ownerId, Guid backupId, CancellationToken ct = default)
    {
        var backup = await db.Backups
            .Where(b => b.Id == backupId && b.OwnerId == ownerId)
            .Select(b => new { b.Content, b.CreationTime })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Backup not found.");

        return (Decompress(backup.Content), backup.CreationTime);
    }

    public async Task DeleteAsync(Guid ownerId, Guid backupId, CancellationToken ct = default)
    {
        // Deleted outright rather than soft-deleted: a snapshot is a copy of data that still
        // exists elsewhere, and keeping the bytes of one somebody asked to be rid of is the
        // opposite of what they asked for.
        var removed = await db.Backups
            .Where(b => b.Id == backupId && b.OwnerId == ownerId)
            .ExecuteDeleteAsync(ct);

        if (removed == 0)
        {
            throw new NotFoundException("Backup not found.");
        }
    }

    /// <summary>Drops everything past the newest <see cref="Backup.KeepPerUser"/>.</summary>
    private async Task TrimAsync(Guid ownerId, CancellationToken ct)
    {
        var keep = await db.Backups
            .Where(b => b.OwnerId == ownerId)
            .OrderByDescending(b => b.CreationTime)
            .Take(Backup.KeepPerUser)
            .Select(b => b.Id)
            .ToListAsync(ct);

        await db.Backups
            .Where(b => b.OwnerId == ownerId && !keep.Contains(b.Id))
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Gzip, because an export is JSON with the same twenty keys repeated once per link and
    /// compresses to a small fraction of itself.
    /// </summary>
    public static byte[] Compress(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }

        return output.ToArray();
    }

    public static string Decompress(byte[] content)
    {
        using var input = new MemoryStream(content);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
