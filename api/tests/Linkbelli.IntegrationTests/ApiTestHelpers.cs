using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Linkbelli.IntegrationTests;

/// <summary>Shared request helpers + lightweight response DTOs for the integration tests.</summary>
public static class ApiTestHelpers
{
    public const string Password = "Passw0rd!";

    public static string NewUsername() => "u" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Registers a fresh user and returns a bearer access token.</summary>
    public static async Task<string> RegisterAndLoginAsync(this HttpClient client, string username)
    {
        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { username, email = $"{username}@example.com", password = Password });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        login.EnsureSuccessStatusCode();

        var token = await login.Content.ReadFromJsonAsync<TokenDto>();
        return token!.AccessToken;
    }

    public record TokenDto(string TokenType, string AccessToken, int ExpiresIn, string RefreshToken);

    public record PlaylistDto(Guid Id, string Name, string Slug, string? Description, string Visibility, int ItemCount, string[] Tags);

    public record ItemDto(Guid Id, long Position);

    public record PagedDto<T>(List<T> Items, string? NextCursor);

    public record ApiKeyCreatedDto(Guid Id, string Name, string Prefix, string Token, string[] Scopes);

    public record PublicSummaryDto(string OwnerUsername, string Slug, string Name, string? Description, int ItemCount, string[] Tags);

    public record TagSummaryDto(string Name, int PlaylistCount);

    public record SourceDto(Guid Id, string Name, string Type, string Visibility, Guid[] PlaylistIds);

    public record SharedSourceDto(Guid Id, string Name, string Type, string OwnerUsername);

    public record AttachedSourceDto(Guid Id, string Name, string Type, string OwnerUsername, string Visibility, bool OwnedByMe);

    public record LinkPreviewDto(string CanonicalUrl, string Host, string? Title, string? Description, string? ImageUrl, string? SiteName);
}

/// <summary>
/// Seeds playlist items straight into the database, already stamped as enriched. The read
/// endpoints only surface enriched links, and enrichment needs a live fetch, so tests that
/// assert on item listing have to bypass the pipeline rather than wait on it.
/// </summary>
public static class ItemSeeder
{
    public static async Task<List<Guid>> SeedEnrichedItemsAsync(
        this PostgresApiFactory factory,
        Guid playlistId,
        int count,
        Func<int, string>? title = null,
        Func<int, string>? url = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var tag = Guid.NewGuid().ToString("N")[..8];

        // Host rows are get-or-created per hostname, exactly as LinkService does — a seeded link
        // at another host has to actually be on that host, or any host-scoped assertion is a lie.
        var hosts = new Dictionary<string, Host>(StringComparer.Ordinal);
        async Task<Host> HostForAsync(string hostname)
        {
            if (hosts.TryGetValue(hostname, out var cached)) return cached;

            var existing = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname == hostname);
            if (existing is null)
            {
                existing = new Host { Hostname = hostname };
                db.Hosts.Add(existing);
                await db.SaveChangesAsync();
            }

            hosts[hostname] = existing;
            return existing;
        }

        var nextPosition = await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId)
            .MaxAsync(i => (long?)i.Position) ?? 0;

        var seeded = new List<PlaylistItem>();
        for (var n = 0; n < count; n++)
        {
            var canonical = url?.Invoke(n) ?? $"https://seed.example/{tag}/{n}";
            UrlCanonicalizer.TryCanonicalize(canonical, out var c);
            var host = await HostForAsync(c.Host);

            // Links are globally deduplicated, so seeding a URL that already exists has to reuse
            // it — otherwise the seeder can't express the same link saved to two playlists, which
            // is a perfectly real state of the system.
            var link = await db.Links.FirstOrDefaultAsync(l => l.UrlHash == c.Hash);
            if (link is null)
            {
                link = new Link
                {
                    CanonicalUrl = c.Url,
                    UrlHash = c.Hash,
                    HostId = host.Id,
                    Title = title?.Invoke(n) ?? $"Seeded item {n}",
                    EnrichedAt = DateTimeOffset.UtcNow,
                };
                db.Links.Add(link);
            }

            nextPosition += PlaylistItem.PositionGap;
            var item = new PlaylistItem
            {
                PlaylistId = playlistId,
                Link = link,
                Position = nextPosition,
            };
            db.PlaylistItems.Add(item);
            seeded.Add(item);
        }

        // Ids are read after SaveChanges so a database-generated key is the one returned.
        await db.SaveChangesAsync();
        return seeded.Select(i => i.Id).ToList();
    }

    /// <summary>
    /// Gives every named playlist the same creation time, to the tick.
    /// </summary>
    /// <remarks>
    /// Created one at a time over HTTP they are microseconds apart, which is enough for a
    /// timestamp alone to order them — so a paging bug that only appears on ties would never
    /// show. An import creating fifty playlists in one SaveChanges produces exactly this state,
    /// so it is a real shape rather than a contrived one.
    ///
    /// Written with ExecuteUpdate rather than by loading each row: the SaveChanges interceptor
    /// stamps LastModified on anything tracked and modified, which would undo the point.
    /// </remarks>
    public static async Task SettleActivityAsync(
        this PostgresApiFactory factory, IReadOnlyList<Guid> playlistIds)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var shared = DateTimeOffset.UtcNow;
        await db.Playlists
            .Where(p => playlistIds.Contains(p.Id))
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.CreationTime, shared));
    }
}
