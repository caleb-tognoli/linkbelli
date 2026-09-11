using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Reordering. A move reads only the two neighbours now, so these pin the resulting order in
/// every direction — including the renumber path, which is the one case that still needs the
/// whole list.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemMoveTests(PostgresApiFactory factory)
{
    private record PagedItemsDto(List<ItemDto> Items, string? NextCursor, int? Total);

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

    private static async Task<List<Guid>> OrderAsync(HttpClient client, Guid playlistId)
    {
        var res = await client.GetAsync($"/api/v1/playlists/{playlistId}/items?limit=100");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<PagedItemsDto>())!;
        return page.Items.Select(i => i.Id).ToList();
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid itemId, Guid? afterItemId) =>
        client.PostAsJsonAsync($"/api/v1/items/{itemId}/move", new { afterItemId });

    [Fact]
    public async Task An_item_can_be_moved_to_the_front()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "To front");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        (await MoveAsync(client, seeded[3], null)).EnsureSuccessStatusCode();

        Assert.Equal([seeded[3], seeded[0], seeded[1], seeded[2]], await OrderAsync(client, playlist));
    }

    [Fact]
    public async Task An_item_can_be_moved_between_two_others()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Between");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        // Last item lands directly after the first.
        (await MoveAsync(client, seeded[3], seeded[0])).EnsureSuccessStatusCode();

        Assert.Equal([seeded[0], seeded[3], seeded[1], seeded[2]], await OrderAsync(client, playlist));
    }

    [Fact]
    public async Task An_item_can_be_moved_to_the_end()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "To end");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 4);

        (await MoveAsync(client, seeded[0], seeded[3])).EnsureSuccessStatusCode();

        Assert.Equal([seeded[1], seeded[2], seeded[3], seeded[0]], await OrderAsync(client, playlist));
    }

    [Fact]
    public async Task Moving_an_item_after_the_one_already_before_it_changes_nothing()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "No-op move");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        (await MoveAsync(client, seeded[1], seeded[0])).EnsureSuccessStatusCode();

        Assert.Equal(seeded, await OrderAsync(client, playlist));
    }

    [Fact]
    public async Task Adjacent_positions_trigger_a_renumber_rather_than_a_failure()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Renumber");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        // Squeeze the first two together so there is no integer room between them.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var items = await db.PlaylistItems.Where(i => i.PlaylistId == playlist)
                .OrderBy(i => i.Position).ToListAsync();
            items[0].Position = 10;
            items[1].Position = 11;
            items[2].Position = 12;
            await db.SaveChangesAsync();
        }

        (await MoveAsync(client, seeded[2], seeded[0])).EnsureSuccessStatusCode();

        Assert.Equal([seeded[0], seeded[2], seeded[1]], await OrderAsync(client, playlist));

        // The renumber respaced everything, so the next move has room again.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var positions = await db.PlaylistItems.Where(i => i.PlaylistId == playlist)
                .OrderBy(i => i.Position).Select(i => i.Position).ToListAsync();

            Assert.Equal([1024L, 2048L, 3072L], positions);
        }
    }

    [Fact]
    public async Task Moving_a_single_item_playlist_is_harmless()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Only one");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        (await MoveAsync(client, seeded[0], null)).EnsureSuccessStatusCode();

        Assert.Equal(seeded, await OrderAsync(client, playlist));
    }

    [Fact]
    public async Task An_item_cannot_be_moved_after_itself()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "After itself");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        var res = await MoveAsync(client, seeded[0], seeded[0]);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_item_cannot_be_moved_after_one_in_a_different_playlist()
    {
        var client = await NewUserAsync();
        var here = await NewPlaylistAsync(client, "Here");
        var elsewhere = await NewPlaylistAsync(client, "Elsewhere");
        var mine = await factory.SeedEnrichedItemsAsync(here, 2);
        var theirs = await factory.SeedEnrichedItemsAsync(elsewhere, 1);

        var res = await MoveAsync(client, mine[0], theirs[0]);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task One_user_cannot_reorder_anothers_playlist()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var playlist = await NewPlaylistAsync(owner, "Not yours");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        var res = await MoveAsync(stranger, seeded[1], null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Repeated_moves_keep_the_order_consistent()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Shuffling");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 6);

        // Walk the last item forward one slot at a time; it should end up first.
        (await MoveAsync(client, seeded[5], seeded[3])).EnsureSuccessStatusCode();
        (await MoveAsync(client, seeded[5], seeded[1])).EnsureSuccessStatusCode();
        (await MoveAsync(client, seeded[5], null)).EnsureSuccessStatusCode();

        var order = await OrderAsync(client, playlist);

        Assert.Equal(seeded[5], order[0]);
        Assert.Equal(6, order.Count);
        Assert.Equal(6, order.Distinct().Count());
    }
}
