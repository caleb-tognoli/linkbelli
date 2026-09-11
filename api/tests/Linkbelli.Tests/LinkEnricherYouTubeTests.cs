using System.Net;
using System.Text;
using System.Text.Json;
using Linkbelli.Application.Auth;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Http;
using Linkbelli.Application.Identity;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Linkbelli.Tests;

public class LinkEnricherYouTubeTests
{
    [Fact]
    public async Task Youtube_link_is_enriched_from_oembed_json()
    {
        const string oembedJson = """
            {
              "title": "Rick Astley - Never Gonna Give You Up",
              "author_name": "Rick Astley",
              "author_url": "https://www.youtube.com/@RickAstleyYT",
              "type": "video",
              "provider_name": "YouTube",
              "thumbnail_url": "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg"
            }
            """;

        var (enricher, db, handler) = BuildEnricher(HttpStatusCode.OK, oembedJson);

        var hostId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        db.Hosts.Add(new Host { Id = hostId, Hostname = "www.youtube.com" });
        db.Links.Add(new Link
        {
            Id = linkId,
            CanonicalUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            UrlHash = new string('a', 64),
            HostId = hostId,
        });
        await db.SaveChangesAsync();

        await enricher.EnrichAsync(linkId);

        var link = await db.Links.FindAsync(linkId);
        Assert.NotNull(link);
        Assert.Equal("Rick Astley - Never Gonna Give You Up", link!.Title);
        Assert.Equal("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg", link.ThumbnailUrl);
        Assert.Equal("YouTube", link.SiteName);
        Assert.NotNull(link.EnrichedAt);

        var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(link.Metadata!)!;
        Assert.Equal("Rick Astley", meta["author"]);
        Assert.Equal("Rick Astley - Never Gonna Give You Up", meta["title"]);
        Assert.Equal("YouTube", meta["provider_name"]);

        // Verify it hit oEmbed, not the raw watch page.
        Assert.Single(handler.Requests);
        Assert.Contains("/oembed", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.Contains("v%3DdQw4w9WgXcQ", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Youtube_link_with_404_from_oembed_is_permanently_stamped()
    {
        var (enricher, db, _) = BuildEnricher(HttpStatusCode.NotFound, "");

        var hostId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        db.Hosts.Add(new Host { Id = hostId, Hostname = "youtube.com" });
        db.Links.Add(new Link
        {
            Id = linkId,
            CanonicalUrl = "https://youtube.com/watch?v=deleted",
            UrlHash = new string('b', 64),
            HostId = hostId,
        });
        await db.SaveChangesAsync();

        await enricher.EnrichAsync(linkId); // should NOT throw

        var link = await db.Links.FindAsync(linkId);
        Assert.NotNull(link!.EnrichedAt);
        Assert.Null(link.Title);

        // A 404 from oEmbed means the video is private, deleted or unlisted — the thing the link
        // pointed at is gone, which is a different state from "we couldn't fetch it".
        Assert.Equal(EnrichmentStatus.Broken, link.EnrichmentStatus);
        Assert.Contains("could not be found", link.EnrichmentError);

        // The reason lives in its own column now, not smuggled through the OpenGraph bag.
        Assert.Null(link.Metadata);
    }

    [Fact]
    public async Task Youtube_link_with_5xx_from_oembed_throws_for_retry()
    {
        var (enricher, db, _) = BuildEnricher(HttpStatusCode.ServiceUnavailable, "");

        var hostId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        db.Hosts.Add(new Host { Id = hostId, Hostname = "www.youtube.com" });
        db.Links.Add(new Link
        {
            Id = linkId,
            CanonicalUrl = "https://www.youtube.com/watch?v=abc",
            UrlHash = new string('c', 64),
            HostId = hostId,
        });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<HttpRequestException>(() => enricher.EnrichAsync(linkId));
    }

    // --- test infrastructure ---

    private static (LinkEnricher enricher, TestDbContext db, StubHandler handler) BuildEnricher(
        HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var client = new HttpClient(handler);
        var factory = new SingleClientFactory(client);
        var db = new TestDbContext();
        var enricher = new LinkEnricher(
            factory,
            new LinkMetadataExtractor(),
            new ArticleExtractor(),
            db,
            new NullThrottle(),
            NullLogger<LinkEnricher>.Instance);
        return (enricher, db, handler);
    }

    private sealed class NullThrottle : IHostThrottle
    {
        public Task WaitAsync(string hostname, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => name == EnrichmentHttpClient.Name ? client : throw new InvalidOperationException($"unexpected client: {name}");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}

/// <summary>
/// Minimal in-memory DbContext for exercising <see cref="LinkEnricher"/>. Only the DbSets the
/// enricher actually reads/writes need real backing; the rest satisfy the interface with
/// empty sets so we don't drag Infrastructure into the unit-test project.
/// </summary>
internal sealed class TestDbContext : DbContext, IAppDbContext
{
    public TestDbContext() : base(new DbContextOptionsBuilder<TestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options) { }

    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
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
    public DbSet<SourceTemplate> SourceTemplates => Set<SourceTemplate>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FolderPlaylist> FolderPlaylists => Set<FolderPlaylist>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Transactions are not needed in these unit tests.");

    public Task SeedRandomAsync(double seed, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public new EntityEntry Entry(object entity) => base.Entry(entity);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Jsonb-mapped complex properties can't be represented by the InMemory provider; the
        // enricher never touches these tables, so unmap them to keep the test context compiling.
        modelBuilder.Entity<PlaylistItem>().Ignore(p => p.Metadata);
        modelBuilder.Entity<Source>().Ignore(s => s.Runs);
        base.OnModelCreating(modelBuilder);
    }
}
