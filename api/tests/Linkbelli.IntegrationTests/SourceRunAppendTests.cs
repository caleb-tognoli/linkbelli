using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Drives SourceRunner end to end against a stub interpreter, so the append/dedup path is covered
/// without a live fetch. Guards the batched membership + next-position reads: a run must add each
/// discovered link once per attached playlist and continue that playlist's numbering.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceRunAppendTests(PostgresApiFactory factory)
{
    /// <summary>Stands in for the RSS interpreter; yields whatever links the test sets.</summary>
    private sealed class StubInterpreter : ISourceInterpreter
    {
        public static List<DiscoveredLink> Links { get; set; } = [];

        public SourceType Type => SourceType.Rss;

        public void ValidateConfig(IReadOnlyDictionary<string, string> config) { }

        public Task<SourceFetchResult> FetchAsync(
            IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceFetchResult(Links));
    }

    private WebApplicationFactory<Program> WithStub() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISourceInterpreter>();
            services.AddSingleton<ISourceInterpreter, StubInterpreter>();
        }));

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<int> ItemCountAsync(LinkbelliDbContext db, Guid playlistId) =>
        await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlistId);

    [Fact]
    public async Task A_run_appends_every_discovered_link_to_each_attached_playlist_exactly_once()
    {
        using var app = WithStub();
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await NewPlaylistAsync(client, "Run target A");
        var second = await NewPlaylistAsync(client, "Run target B");

        // The second playlist starts with items of its own, so the run has to continue its
        // numbering rather than restart at the gap.
        await factory.SeedEnrichedItemsAsync(second, 3);

        var tag = Guid.NewGuid().ToString("N")[..8];
        StubInterpreter.Links =
        [
            new DiscoveredLink($"https://run.example/{tag}/1", "One"),
            new DiscoveredLink($"https://run.example/{tag}/2", "Two"),
            // A duplicate inside one fetch must not produce two rows.
            new DiscoveredLink($"https://run.example/{tag}/2", "Two again"),
        ];

        var created = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Stub source",
            type = "Rss",
            config = new { feedUrl = "https://run.example/feed.xml" },
            schedule = "0 * * * *",
            playlistIds = new[] { first, second },
        });
        created.EnsureSuccessStatusCode();
        var source = (await created.Content.ReadFromJsonAsync<SourceDto>())!;

        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISourceRunner>().RunAsync(source.Id);
        }

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

            Assert.Equal(2, await ItemCountAsync(db, first));
            Assert.Equal(5, await ItemCountAsync(db, second)); // 3 seeded + 2 discovered

            // Appended items continue past the seeded ones rather than colliding with them.
            var rows = await db.PlaylistItems
                .Where(i => i.PlaylistId == second)
                .Select(i => new { i.Position, i.SourceId })
                .ToListAsync();

            Assert.Equal(rows.Count, rows.Select(r => r.Position).Distinct().Count());

            var seededMax = rows.Where(r => r.SourceId == null).Max(r => r.Position);
            var appendedMin = rows.Where(r => r.SourceId != null).Min(r => r.Position);
            Assert.True(appendedMin > seededMax,
                $"Discovered items should be appended after existing ones ({appendedMin} <= {seededMax}).");
        }
    }

    [Fact]
    public async Task Re_running_a_source_over_the_same_links_adds_nothing()
    {
        using var app = WithStub();
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var playlist = await NewPlaylistAsync(client, "Idempotent run");

        var tag = Guid.NewGuid().ToString("N")[..8];
        StubInterpreter.Links =
        [
            new DiscoveredLink($"https://rerun.example/{tag}/a", "A"),
            new DiscoveredLink($"https://rerun.example/{tag}/b", "B"),
        ];

        var created = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Stub rerun",
            type = "Rss",
            config = new { feedUrl = "https://rerun.example/feed.xml" },
            schedule = "0 * * * *",
            playlistIds = new[] { playlist },
        });
        created.EnsureSuccessStatusCode();
        var source = (await created.Content.ReadFromJsonAsync<SourceDto>())!;

        for (var run = 0; run < 3; run++)
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISourceRunner>().RunAsync(source.Id);
        }

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            Assert.Equal(2, await ItemCountAsync(db, playlist));
        }
    }
}
