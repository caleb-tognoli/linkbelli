using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Export;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Putting a backup back.
/// </summary>
/// <remarks>
/// A backup system with no restore is a file-copying system. Everything else was here — weekly
/// snapshots, five-deep retention, content hashing so an unchanged library is not stored twice —
/// and the one thing the whole apparatus exists for was missing. The app said so in its own UI,
/// which was honest and did not make it any less missing.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RestoreTests(PostgresApiFactory factory)
{
    private record Plan(
        bool DryRun,
        int FormatVersion,
        int FoldersAdded,
        int PlaylistsAdded,
        int PlaylistsMatched,
        int ItemsAdded,
        int ItemsAlreadyThere,
        int SourcesAdded,
        bool Truncated,
        bool SourcesNeedCredentials);

    private record ItemDto(Guid Id, string? Note, int? Score, string Status, string[] Tags, LinkRef Link);

    private record LinkRef(Guid Id, string Url);

    private record PagedItems(List<ItemDto> Items);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private static async Task<string> ExportAsync(HttpClient client) =>
        await client.GetStringAsync("/api/v1/export?format=json");

    private static async Task<Plan> RestoreAsync(HttpClient client, string json, bool dryRun)
    {
        var res = await client.PostAsJsonAsync("/api/v1/backups/restore", new { json, dryRun });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Plan>())!;
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    [Fact]
    public async Task An_export_now_says_what_shape_it_is()
    {
        var client = await NewUserAsync();
        await NewPlaylistAsync(client, $"Versioned {Guid.NewGuid():N}");

        var json = await ExportAsync(client);

        Assert.Contains($"\"version\":{ExportFormatVersion.Current}", json.Replace(" ", ""));
    }

    /// <summary>
    /// The whole point of the dry run: being told what it will do while it is still possible to
    /// decide otherwise.
    /// </summary>
    [Fact]
    public async Task A_dry_run_counts_and_writes_nothing()
    {
        var author = await NewUserAsync();
        var playlist = await NewPlaylistAsync(author, $"Backed up {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        var json = await ExportAsync(author);

        var fresh = await NewUserAsync();
        var plan = await RestoreAsync(fresh, json, dryRun: true);

        Assert.True(plan.DryRun);
        Assert.Equal(1, plan.PlaylistsAdded);
        Assert.Equal(3, plan.ItemsAdded);

        // Nothing written: the library it was tried against is still empty.
        var theirs = await fresh.GetFromJsonAsync<PagedPlaylists>("/api/v1/playlists");
        Assert.Empty(theirs!.Items);
    }

    [Fact]
    public async Task A_restore_puts_the_playlists_and_their_links_back()
    {
        var author = await NewUserAsync();
        var playlist = await NewPlaylistAsync(author, $"Recovered {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 2);
        var original = await ItemsAsync(author, playlist.Id);

        var json = await ExportAsync(author);

        var fresh = await NewUserAsync();
        var plan = await RestoreAsync(fresh, json, dryRun: false);

        Assert.False(plan.DryRun);
        Assert.Equal(1, plan.PlaylistsAdded);
        Assert.Equal(2, plan.ItemsAdded);
        Assert.Equal(seeded.Count, original.Count);

        var restoredPlaylists = await fresh.GetFromJsonAsync<PagedPlaylists>("/api/v1/playlists");
        var restored = Assert.Single(restoredPlaylists!.Items);
        Assert.Equal(playlist.Name, restored.Name);

        var items = await ItemsAsync(fresh, restored.Id);
        Assert.Equal(
            original.Select(i => i.Link.Url).Order(),
            items.Select(i => i.Link.Url).Order());
    }

    /// <summary>
    /// Notes, scores and reading state are the point of restoring rather than re-importing a
    /// list of addresses — importing recovers URLs and loses everything that made them yours.
    /// </summary>
    [Fact]
    public async Task Notes_scores_status_and_tags_come_back_too()
    {
        var author = await NewUserAsync();
        var playlist = await NewPlaylistAsync(author, $"Annotated {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        (await author.PatchAsJsonAsync(
            $"/api/v1/items/{seeded[0]}",
            new { note = "Worth rereading", status = "Watched", tags = new[] { "keep" } }))
            .EnsureSuccessStatusCode();
        (await author.PutAsJsonAsync($"/api/v1/items/{seeded[0]}/score", new { score = 90 }))
            .EnsureSuccessStatusCode();

        var json = await ExportAsync(author);

        var fresh = await NewUserAsync();
        await RestoreAsync(fresh, json, dryRun: false);

        var restored = Assert.Single((await fresh.GetFromJsonAsync<PagedPlaylists>("/api/v1/playlists"))!.Items);
        var item = Assert.Single(await ItemsAsync(fresh, restored.Id));

        Assert.Equal("Worth rereading", item.Note);
        Assert.Equal(90, item.Score);
        Assert.Equal("Watched", item.Status);
        Assert.Contains("keep", item.Tags);
    }

    /// <summary>
    /// Merge, not replace. Wiping and rewriting would throw away everything saved since the
    /// snapshot, which is a far more destructive thing than most people asking for a restore
    /// have in mind.
    /// </summary>
    [Fact]
    public async Task Restoring_over_a_library_adds_what_is_missing_and_leaves_the_rest()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Growing {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        var json = await ExportAsync(client);

        // Two more links arrive after the snapshot, and one of the originals gets a note.
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);
        var before = await ItemsAsync(client, playlist.Id);
        (await client.PatchAsJsonAsync($"/api/v1/items/{before[0].Id}", new { note = "Since the backup" }))
            .EnsureSuccessStatusCode();

        var plan = await RestoreAsync(client, json, dryRun: false);

        Assert.Equal(1, plan.PlaylistsMatched);
        Assert.Equal(0, plan.PlaylistsAdded);
        Assert.Equal(2, plan.ItemsAlreadyThere);
        Assert.Equal(0, plan.ItemsAdded);

        var after = await ItemsAsync(client, playlist.Id);
        Assert.Equal(4, after.Count);

        // And the note written after the snapshot survives: a restore should not undo the
        // reading somebody has done since.
        Assert.Equal("Since the backup", after.Single(i => i.Id == before[0].Id).Note);
    }

    [Fact]
    public async Task Folders_come_back_with_their_nesting()
    {
        var author = await NewUserAsync();

        var parent = await author.PostAsJsonAsync("/api/v1/folders", new { name = $"Parent {Guid.NewGuid():N}" });
        parent.EnsureSuccessStatusCode();
        var parentId = (await parent.Content.ReadFromJsonAsync<FolderDto>())!.Id;

        var child = await author.PostAsJsonAsync(
            "/api/v1/folders", new { name = $"Child {Guid.NewGuid():N}", parentId });
        child.EnsureSuccessStatusCode();

        var json = await ExportAsync(author);

        var fresh = await NewUserAsync();
        var plan = await RestoreAsync(fresh, json, dryRun: false);

        Assert.Equal(2, plan.FoldersAdded);

        var folders = await fresh.GetFromJsonAsync<List<FolderDto>>("/api/v1/folders");
        var restoredParent = Assert.Single(folders!, f => f.ParentId == null);
        Assert.Single(folders!, f => f.ParentId == restoredParent.Id);
    }

    /// <summary>
    /// A restore that silently drops fields it does not understand is worse than one that
    /// declines to run.
    /// </summary>
    [Fact]
    public async Task A_file_from_a_newer_version_is_refused_rather_than_half_read()
    {
        var client = await NewUserAsync();
        var json = (await ExportAsync(client))
            .Replace($"\"version\":{ExportFormatVersion.Current}", "\"version\":9999")
            .Replace($"\"version\": {ExportFormatVersion.Current}", "\"version\": 9999");

        var res = await client.PostAsJsonAsync("/api/v1/backups/restore", new { json, dryRun = true });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("newer version", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Something_that_is_not_a_backup_is_refused()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync(
            "/api/v1/backups/restore", new { json = "{\"hello\":true}", dryRun = true });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// A stored snapshot, which is the path the UI actually uses — and the one that proves the
    /// backup and the restore agree about the format.
    /// </summary>
    [Fact]
    public async Task A_stored_snapshot_can_be_previewed_and_restored()
    {
        var author = await NewUserAsync();
        var playlist = await NewPlaylistAsync(author, $"Snapshotted {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        var made = await author.PostAsync("/api/v1/backups", null);
        made.EnsureSuccessStatusCode();
        var backup = (await made.Content.ReadFromJsonAsync<BackupDto>())!;

        // Its own library already holds everything, so nothing is added — which is the correct
        // answer and proves the matching works rather than proving nothing.
        var preview = await author.GetFromJsonAsync<Plan>($"/api/v1/backups/{backup.Id}/restore");

        Assert.True(preview!.DryRun);
        Assert.Equal(1, preview.PlaylistsMatched);
        Assert.Equal(2, preview.ItemsAlreadyThere);
        Assert.Equal(0, preview.ItemsAdded);
    }

    /// <summary>
    /// Sources come back paused: their config left in the export with its secrets redacted, so
    /// one that starts fetching on a schedule with half a config just fails on a timer.
    /// </summary>
    [Fact]
    public async Task A_restored_source_arrives_paused_and_says_it_may_need_credentials()
    {
        var author = await NewUserAsync();
        (await author.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Feed {Guid.NewGuid():N}",
            type = "Rss",
            config = new Dictionary<string, string> { ["feedUrl"] = $"https://f{Guid.NewGuid():N}.example/rss" },
            schedule = "0 6 * * *",
            playlistIds = Array.Empty<Guid>(),
        })).EnsureSuccessStatusCode();

        var json = await ExportAsync(author);

        var fresh = await NewUserAsync();
        var plan = await RestoreAsync(fresh, json, dryRun: false);

        Assert.Equal(1, plan.SourcesAdded);
        Assert.True(plan.SourcesNeedCredentials);

        var sources = await fresh.GetFromJsonAsync<List<SourceStateDto>>("/api/v1/sources");
        Assert.Equal("Paused", Assert.Single(sources!).Status);
    }

    private record PagedPlaylists(List<PlaylistDto> Items);

    private record FolderDto(Guid Id, string Name, Guid? ParentId);

    private record BackupDto(Guid Id);

    private record SourceStateDto(Guid Id, string Name, string Status);
}
