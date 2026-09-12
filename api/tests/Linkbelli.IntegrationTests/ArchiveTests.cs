using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Archiving;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A saved link outlives the page behind it, and a snapshot is what someone can still be sent to
/// once the original is gone. These cover who gets archived — which is the whole design, since
/// asking for a snapshot tells a third party what someone saved.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ArchiveTests(PostgresApiFactory factory)
{
    /// <summary>Stands in for the Internet Archive, without asking it for anything.</summary>
    private sealed class StubArchiver : IArchiver
    {
        public static List<string> Asked { get; } = [];

        /// <summary>What to answer with. Null means "refused, don't come back".</summary>
        public static ArchiveResult Answer { get; set; } =
            new("https://web.archive.org/web/1/https://example.com/", Retry: false);

        public Task<ArchiveResult> ArchiveAsync(string url, CancellationToken cancellationToken = default)
        {
            Asked.Add(url);
            return Task.FromResult(Answer);
        }
    }

    private record ItemDto(Guid Id, LinkDto Link);

    private record LinkDto(Guid Id, string Url, string? ArchiveUrl);

    private record PagedItems(List<ItemDto> Items);

    private record MeDto(bool ShowNsfw, bool ArchiveLinks);

    private WebApplicationFactory<Program> WithStub()
    {
        StubArchiver.Asked.Clear();
        StubArchiver.Answer = new("https://web.archive.org/web/1/https://example.com/", Retry: false);

        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IArchiver>();
            services.AddScoped<IArchiver, StubArchiver>();
        }));
    }

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task OptInAsync(HttpClient client, bool archive = true) =>
        (await client.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = false, archiveLinks = archive }))
            .EnsureSuccessStatusCode();

    /// <summary>Seeds a link the way a successful enrichment leaves one.</summary>
    private async Task<Guid> SeedLinkAsync(Guid playlistId)
    {
        var (linkId, _) = await SeedLinkWithUrlAsync(playlistId);
        return linkId;
    }

    /// <summary>
    /// The seeded link and the address it was given.
    /// </summary>
    /// <remarks>
    /// The address matters because the sweep picks up a batch of links belonging to anybody who
    /// has opted in, and <see cref="StubArchiver.Asked" /> is shared across the whole class. A
    /// test that counts the total is really asserting about every other test's links too.
    /// </remarks>
    private async Task<(Guid LinkId, string Url)> SeedLinkWithUrlAsync(Guid playlistId)
    {
        var url = $"https://archive.example/{Guid.NewGuid():N}";
        var seeded = await factory.SeedEnrichedItemsAsync(playlistId, 1, url: _ => url);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var item = await db.PlaylistItems.Include(i => i.Link).FirstAsync(i => i.Id == seeded[0]);
        item.Link!.EnrichmentStatus = EnrichmentStatus.Succeeded;
        await db.SaveChangesAsync();

        return (item.LinkId, item.Link.CanonicalUrl);
    }

    private static async Task SweepAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ILinkArchiveSweep>().SweepAsync();
    }

    private async Task<Link> ReadLinkAsync(Guid linkId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Links.AsNoTracking().FirstAsync(l => l.Id == linkId);
    }

    [Fact]
    public async Task Nothing_is_archived_for_someone_who_did_not_ask()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        var playlist = await NewPlaylistAsync(client, "Not asked");
        var linkId = await SeedLinkAsync(playlist);

        await SweepAsync(app);

        // Off by default and it stays off: asking for a snapshot tells a third party what this
        // person saved, which is their choice to make.
        Assert.Null((await ReadLinkAsync(linkId)).ArchiveUrl);
        Assert.DoesNotContain(StubArchiver.Asked, url => url.Contains("archive.example"));
    }

    [Fact]
    public async Task A_link_is_archived_once_its_owner_asks()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        var playlist = await NewPlaylistAsync(client, "Asked");
        var linkId = await SeedLinkAsync(playlist);

        await SweepAsync(app);

        var link = await ReadLinkAsync(linkId);
        Assert.StartsWith("https://web.archive.org/", link.ArchiveUrl);
        Assert.NotNull(link.ArchivedAt);
    }

    [Fact]
    public async Task The_snapshot_is_offered_on_the_link_itself()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        var playlist = await NewPlaylistAsync(client, "Shown");
        await SeedLinkAsync(playlist);

        await SweepAsync(app);

        var items = await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items");
        Assert.StartsWith("https://web.archive.org/", items!.Items[0].Link.ArchiveUrl);
    }

    [Fact]
    public async Task An_already_archived_link_is_not_asked_about_again()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        var playlist = await NewPlaylistAsync(client, "Once");
        var (_, url) = await SeedLinkWithUrlAsync(playlist);

        await SweepAsync(app);
        await SweepAsync(app);

        // Counted for this link alone. The total would also count every other test's links, which
        // the same sweep picks up — and which is what made this flake as the suite grew.
        Assert.Equal(1, StubArchiver.Asked.Count(asked => asked == url));
    }

    [Fact]
    public async Task A_page_the_archive_refuses_is_given_up_on()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        var playlist = await NewPlaylistAsync(client, "Refused");
        var linkId = await SeedLinkAsync(playlist);

        StubArchiver.Answer = new(null, Retry: false);

        for (var attempt = 0; attempt < Link.MaxArchiveAttempts + 2; attempt++)
        {
            await SweepAsync(app);
        }

        // Paywalls and robots.txt refusals are permanent; retrying them forever would spend the
        // whole budget on pages that will never be taken.
        Assert.Equal(Link.MaxArchiveAttempts, (await ReadLinkAsync(linkId)).ArchiveAttempts);
    }

    [Fact]
    public async Task A_rate_limit_is_not_held_against_the_link()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        var playlist = await NewPlaylistAsync(client, "Deferred");
        var linkId = await SeedLinkAsync(playlist);

        StubArchiver.Answer = new(null, Retry: true);
        await SweepAsync(app);
        await SweepAsync(app);

        // Their problem, and it passes. Counting it would use up the link's three chances on a
        // busy afternoon at the archive.
        Assert.Equal(0, (await ReadLinkAsync(linkId)).ArchiveAttempts);
    }

    [Fact]
    public async Task Opting_out_again_stops_it()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);
        await OptInAsync(client, archive: false);
        var playlist = await NewPlaylistAsync(client, "Changed their mind");
        var linkId = await SeedLinkAsync(playlist);

        await SweepAsync(app);

        Assert.Null((await ReadLinkAsync(linkId)).ArchiveUrl);
    }

    [Fact]
    public async Task The_preference_is_reported_back()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);

        Assert.False((await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!.ArchiveLinks);

        await OptInAsync(client);

        Assert.True((await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!.ArchiveLinks);
    }

    [Fact]
    public async Task An_older_client_that_omits_it_does_not_turn_it_off()
    {
        using var app = WithStub();
        var client = await SignedInAsync(app);
        await OptInAsync(client);

        // The NSFW toggle is the only preference an older client knows about.
        (await client.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true }))
            .EnsureSuccessStatusCode();

        Assert.True((await client.GetFromJsonAsync<MeDto>("/api/v1/me"))!.ArchiveLinks);
    }
}
