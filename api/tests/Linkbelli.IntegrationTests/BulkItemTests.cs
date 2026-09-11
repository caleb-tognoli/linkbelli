using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Services;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// One action over a selection. The behaviour that matters beyond "it works": a stale or foreign
/// id is skipped rather than throwing the rest of the batch away, and dedup still holds.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BulkItemTests(PostgresApiFactory factory)
{
    private record BulkResultDto(int Affected, int Skipped);
    private record ItemStateDto(Guid Id, string Status, int? Score);
    private record PagedItemsDto(List<ItemStateDto> Items, string? NextCursor, int? Total);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
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

    private static async Task<BulkResultDto> BulkAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/v1/items/bulk", body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<BulkResultDto>())!;
    }

    private static async Task<List<ItemStateDto>> ItemsAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.GetAsync($"/api/v1/playlists/{playlistId}/items?limit=100&status=All");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PagedItemsDto>())!.Items;
    }

    [Fact]
    public async Task A_selection_can_be_marked_watched_in_one_request()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bulk watched");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        var result = await BulkAsync(client, new
        {
            itemIds = seeded.Take(3),
            action = "SetStatus",
            status = "Watched",
        });

        Assert.Equal(3, result.Affected);

        var items = await ItemsAsync(client, playlist);
        Assert.Equal(3, items.Count(i => i.Status == "Watched"));
        Assert.Equal(1, items.Count(i => i.Status == "Added"));
    }

    [Fact]
    public async Task Items_already_in_that_state_are_not_counted_as_changed()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Already watched");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        await BulkAsync(client, new { itemIds = seeded, action = "SetStatus", status = "Watched" });
        var again = await BulkAsync(client, new { itemIds = seeded, action = "SetStatus", status = "Watched" });

        // Nothing happened to them, and stamping a fresh timestamp would make re-marking look
        // like progress.
        Assert.Equal(0, again.Affected);
    }

    [Fact]
    public async Task A_selection_can_be_scored_and_cleared()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bulk score");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        await BulkAsync(client, new { itemIds = seeded, action = "SetScore", score = 80 });
        Assert.All(await ItemsAsync(client, playlist), i => Assert.Equal(80, i.Score));

        await BulkAsync(client, new { itemIds = seeded, action = "SetScore", score = (int?)null });
        Assert.All(await ItemsAsync(client, playlist), i => Assert.Null(i.Score));
    }

    [Fact]
    public async Task A_selection_can_be_deleted_and_is_recoverable()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bulk delete");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        var result = await BulkAsync(client, new { itemIds = seeded.Take(2), action = "Delete" });

        Assert.Equal(2, result.Affected);
        Assert.Equal(2, (await ItemsAsync(client, playlist)).Count);

        // Soft, like every other delete — so the trash can undo it.
        (await client.PostAsync($"/api/v1/trash/items/{seeded[0]}/restore", null)).EnsureSuccessStatusCode();
        Assert.Equal(3, (await ItemsAsync(client, playlist)).Count);
    }

    [Fact]
    public async Task A_selection_can_be_moved_to_another_playlist()
    {
        var client = await NewUserAsync();
        var from = await NewPlaylistAsync(client, "Source list");
        var to = await NewPlaylistAsync(client, "Target list");
        var seeded = await factory.SeedEnrichedItemsAsync(from, 4);

        var result = await BulkAsync(client, new
        {
            itemIds = seeded.Take(3),
            action = "Move",
            targetPlaylistId = to,
        });

        Assert.Equal(3, result.Affected);
        Assert.Single(await ItemsAsync(client, from));
        Assert.Equal(3, (await ItemsAsync(client, to)).Count);
    }

    [Fact]
    public async Task A_copy_leaves_the_originals_and_carries_the_note_and_score()
    {
        var client = await NewUserAsync();
        var from = await NewPlaylistAsync(client, "Copy from");
        var to = await NewPlaylistAsync(client, "Copy to");
        var seeded = await factory.SeedEnrichedItemsAsync(from, 2);

        (await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = "worth keeping" }))
            .EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/v1/items/{seeded[0]}/score", new { score = 90 }))
            .EnsureSuccessStatusCode();

        var result = await BulkAsync(client, new { itemIds = seeded, action = "Copy", targetPlaylistId = to });

        Assert.Equal(2, result.Affected);
        Assert.Equal(2, (await ItemsAsync(client, from)).Count);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var copied = await db.PlaylistItems.Where(i => i.PlaylistId == to).OrderBy(i => i.Position).ToListAsync();

        Assert.Equal(2, copied.Count);

        // Copies land in the same relative order the selection had, so the first one here is the
        // first one there — note and score carried across, since they are the reader's own work.
        Assert.Equal("worth keeping", copied[0].Note);
        Assert.Equal(90, copied[0].Score);
        Assert.Null(copied[1].Note);
    }

    [Fact]
    public async Task A_move_preserves_the_order_the_selection_had()
    {
        var client = await NewUserAsync();
        var from = await NewPlaylistAsync(client, "Ordered from");
        var to = await NewPlaylistAsync(client, "Ordered to");
        var seeded = await factory.SeedEnrichedItemsAsync(from, 4, title: n => $"Ordered {n}");

        await BulkAsync(client, new { itemIds = seeded, action = "Move", targetPlaylistId = to });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var landed = await db.PlaylistItems
            .Where(i => i.PlaylistId == to)
            .OrderBy(i => i.Position)
            .Select(i => i.Id)
            .ToListAsync();

        Assert.Equal(seeded, landed);
    }

    [Fact]
    public async Task Links_already_in_the_target_are_skipped_not_duplicated()
    {
        var client = await NewUserAsync();
        var from = await NewPlaylistAsync(client, "Dedup from");
        var to = await NewPlaylistAsync(client, "Dedup to");
        var seeded = await factory.SeedEnrichedItemsAsync(from, 2);

        await BulkAsync(client, new { itemIds = seeded, action = "Copy", targetPlaylistId = to });
        var again = await BulkAsync(client, new { itemIds = seeded, action = "Copy", targetPlaylistId = to });

        Assert.Equal(0, again.Affected);
        Assert.Equal(2, again.Skipped);
        Assert.Equal(2, (await ItemsAsync(client, to)).Count);
    }

    [Fact]
    public async Task A_foreign_id_in_the_selection_is_skipped_not_fatal()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "Mine");
        var theirs = await NewPlaylistAsync(stranger, "Theirs");
        var myItems = await factory.SeedEnrichedItemsAsync(mine, 2);
        var theirItems = await factory.SeedEnrichedItemsAsync(theirs, 1);

        var result = await BulkAsync(owner, new
        {
            itemIds = myItems.Concat(theirItems).Append(Guid.NewGuid()),
            action = "SetStatus",
            status = "Watched",
        });

        // A stale id in a selection is a normal thing to happen, not a reason to throw the rest
        // of the batch away.
        Assert.Equal(2, result.Affected);
        Assert.Equal(2, result.Skipped);

        // And the other account is untouched.
        Assert.All(await ItemsAsync(stranger, theirs), i => Assert.Equal("Added", i.Status));
    }

    [Fact]
    public async Task Moving_into_a_playlist_the_caller_does_not_own_is_refused()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var mine = await NewPlaylistAsync(owner, "My items");
        var theirs = await NewPlaylistAsync(stranger, "Their list");
        var seeded = await factory.SeedEnrichedItemsAsync(mine, 2);

        var res = await owner.PostAsJsonAsync("/api/v1/items/bulk", new
        {
            itemIds = seeded,
            action = "Move",
            targetPlaylistId = theirs,
        });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task An_oversized_selection_is_rejected()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/items/bulk", new
        {
            itemIds = Enumerable.Range(0, IBulkItemService.MaxItems + 1).Select(_ => Guid.NewGuid()),
            action = "Delete",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_empty_selection_does_nothing_rather_than_failing()
    {
        var client = await NewUserAsync();

        var result = await BulkAsync(client, new { itemIds = Array.Empty<Guid>(), action = "Delete" });

        Assert.Equal(0, result.Affected);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public async Task A_status_action_without_a_status_is_rejected()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Missing status");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var res = await client.PostAsJsonAsync("/api/v1/items/bulk", new
        {
            itemIds = seeded,
            action = "SetStatus",
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
