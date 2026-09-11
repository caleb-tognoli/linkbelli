using System.Security.Cryptography;
using System.Text;
using Linkbelli.Application.Data;
using Linkbelli.Application.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Enrichment;

/// <summary>A cached thumbnail: the bytes and what they are.</summary>
public record CachedThumbnail(byte[] Content, string ContentType);

/// <summary>
/// Serves link thumbnails from here rather than from the site they came from. Rendering the
/// origin URL directly told every site in a playlist the viewer's IP address and what they were
/// looking at, and broke outright whenever a host refused hotlinking.
/// </summary>
public interface IThumbnailCache
{
    /// <summary>Refuses anything larger, so one enormous image can't fill the cache directory.</summary>
    const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>How long a cached image is served before it is fetched again.</summary>
    const int MaxAgeDays = 30;

    /// <summary>
    /// The thumbnail for a link, fetched and stored on first request. Null when the link has no
    /// thumbnail, or the fetch failed — the caller serves a 404 and the page falls back to the
    /// favicon, which is what it does for a link with no image anyway.
    /// </summary>
    Task<CachedThumbnail?> GetAsync(Guid linkId, CancellationToken ct = default);
}

/// <inheritdoc />
public class ThumbnailCache(
    IAppDbContext db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ThumbnailCache> logger) : IThumbnailCache
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/avif",
    };

    public async Task<CachedThumbnail?> GetAsync(Guid linkId, CancellationToken ct = default)
    {
        var url = await db.Links
            .Where(l => l.Id == linkId && l.ThumbnailUrl != null)
            .Select(l => l.ThumbnailUrl!)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var path = PathFor(url);
        if (TryReadFresh(path, out var cached))
        {
            return cached;
        }

        return await FetchAndStoreAsync(url, path, ct);
    }

    private async Task<CachedThumbnail?> FetchAndStoreAsync(string url, string path, CancellationToken ct)
    {
        try
        {
            // The same SSRF-protected client enrichment uses: a thumbnail URL is attacker-supplied
            // in exactly the way a page URL is.
            var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);
            using var response = await client.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Said out loud, because a thumbnail that never appears is otherwise mute: hosts
                // refusing a bot user agent is the common case and worth being able to see.
                logger.LogInformation(
                    "Thumbnail fetch for {Url} returned HTTP {Status}.", url, (int)response.StatusCode);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null || !AllowedTypes.Contains(contentType))
            {
                // Only image types: this endpoint's output is rendered in an <img>, and serving
                // whatever a third-party host returned would be a way to smuggle something else.
                logger.LogInformation(
                    "Thumbnail at {Url} is {ContentType}, which is not an image type we serve.",
                    url, contentType ?? "untyped");
                return null;
            }

            var content = await response.Content.ReadAsByteArrayAsync(ct);
            if (content.Length == 0 || content.Length > IThumbnailCache.MaxBytes)
            {
                logger.LogInformation(
                    "Thumbnail at {Url} is {Bytes} bytes, outside what we cache.", url, content.Length);
                return null;
            }

            await StoreAsync(path, contentType, content, ct);

            return new CachedThumbnail(content, contentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A missing thumbnail is cosmetic — the row still renders with its favicon — but it
            // should not be silent, or an image that never loads has nothing to explain it.
            logger.LogInformation("Could not fetch the thumbnail at {Url}: {Reason}", url, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Reads a cached file when it is still fresh. The content type is stored alongside the bytes
    /// rather than guessed from an extension, because the origin URL often has neither.
    /// </summary>
    private bool TryReadFresh(string path, out CachedThumbnail? cached)
    {
        cached = null;

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-IThumbnailCache.MaxAgeDays))
            {
                return false;
            }

            var typePath = path + ".type";
            var contentType = File.Exists(typePath) ? File.ReadAllText(typePath).Trim() : "image/jpeg";

            cached = new CachedThumbnail(File.ReadAllBytes(path), contentType);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A half-written, locked or unreadable file is worth re-fetching, not failing over.
            return false;
        }
    }

    private async Task StoreAsync(string path, string contentType, byte[] content, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content, ct);
            await File.WriteAllTextAsync(path + ".type", contentType, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Serving it uncached is strictly better than failing the request — which is what a
            // narrower catch did: a permission error on the cache directory escaped and took the
            // whole fetch down with it, so every thumbnail 404'd.
            logger.LogWarning("Could not write the thumbnail cache at {Path}: {Reason}", path, ex.Message);
        }
    }

    /// <summary>
    /// Where a URL's bytes live. Hashed so the filename is fixed-length and safe, and split into
    /// a subdirectory so one flat folder never holds hundreds of thousands of files.
    /// </summary>
    private string PathFor(string url)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(CacheRoot, hash[..2], hash);
    }

    private string CacheRoot =>
        configuration["Thumbnails:CachePath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "linkbelli-thumbnails");
}
