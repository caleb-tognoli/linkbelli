using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Services;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Deletes have always been soft; these cover the way back. Also pins the two collision cases a
/// restore can hit after sitting in the trash: a slug that was taken, and a link that came back.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class TrashTests(PostgresApiFactory factory)
{
    private record TrashedPlaylistDto(Guid Id, string Name, string Slug, int ItemCount, DateTimeOffset DeletedAt);
    private record TrashedItemDto(Guid Id, Guid PlaylistId, string PlaylistName, string Url, string? Title);
    private record TrashDto(List<TrashedPlaylistDto> Playlists, List<TrashedItemDto> Items, int RetentionDays);
    private record PagedItemsDto(List<ItemDto> Items, string? NextCursor, int? Total);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private static async Task<TrashDto> TrashAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/v1/trash");
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<TrashDto>())!;
    }

    [Fact]
    public async Task A_deleted_playlist_can_be_restored_with_its_items()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Recoverable");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        (await client.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/playlists/{playlist.Id}")).StatusCode);

        var trash = await TrashAsync(client);
        var trashed = Assert.Single(trash.Playlists, p => p.Id == playlist.Id);
        Assert.Equal(3, trashed.ItemCount);
        Assert.Equal(30, trash.RetentionDays);

        (await client.PostAsync($"/api/v1/trash/playlists/{playlist.Id}/restore", null)).EnsureSuccessStatusCode();

        (await client.GetAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<PagedItemsDto>($"/api/v1/playlists/{playlist.Id}/items");
        Assert.Equal(3, items!.Items.Count);

        Assert.DoesNotContain((await TrashAsync(client)).Playlists, p => p.Id == playlist.Id);
    }

    [Fact]
    public async Task A_deleted_item_can_be_restored_and_lands_at_the_end()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Item recovery");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();

        var trashed = Assert.Single((await TrashAsync(client)).Items, i => i.Id == seeded[0]);
        Assert.Equal("Item recovery", trashed.PlaylistName);

        (await client.PostAsync($"/api/v1/trash/items/{seeded[0]}/restore", null)).EnsureSuccessStatusCode();

        var items = await client.GetFromJsonAsync<PagedItemsDto>($"/api/v1/playlists/{playlist.Id}/items");
        Assert.Equal(3, items!.Items.Count);
        // Restored to the end, not back into a slot that may have been filled since.
        Assert.Equal(seeded[0], items.Items[^1].Id);
    }

    [Fact]
    public async Task Items_of_a_deleted_playlist_are_not_listed_on_their_own()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Whole playlist");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();

        var trash = await TrashAsync(client);

        Assert.Contains(trash.Playlists, p => p.Id == playlist.Id);
        Assert.DoesNotContain(trash.Items, i => i.PlaylistId == playlist.Id);
    }

    [Fact]
    public async Task Restoring_a_playlist_whose_slug_was_taken_gets_a_suffixed_slug()
    {
        var client = await NewUserAsync();
        var original = await NewPlaylistAsync(client, "Weekend reading");

        (await client.DeleteAsync($"/api/v1/playlists/{original.Id}")).EnsureSuccessStatusCode();

        // A new playlist claims the slug while the old one sits in the trash.
        var replacement = await NewPlaylistAsync(client, "Weekend reading");
        Assert.Equal(original.Slug, replacement.Slug);

        (await client.PostAsync($"/api/v1/trash/playlists/{original.Id}/restore", null)).EnsureSuccessStatusCode();

        var restored = await client.GetFromJsonAsync<PlaylistDto>($"/api/v1/playlists/{original.Id}");
        Assert.NotEqual(replacement.Slug, restored!.Slug);
        Assert.StartsWith(original.Slug, restored.Slug);
    }

    [Fact]
    public async Task Restoring_an_item_whose_link_came_back_on_its_own_is_refused()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Duplicate guard");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        Guid linkId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            linkId = (await db.PlaylistItems.FirstAsync(i => i.Id == seeded[0])).LinkId;
        }

        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();

        // The same link is added again while the old row sits in the trash.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            db.PlaylistItems.Add(new PlaylistItem
            {
                PlaylistId = playlist.Id,
                LinkId = linkId,
                Position = 99_999,
            });
            await db.SaveChangesAsync();
        }

        var restore = await client.PostAsync($"/api/v1/trash/items/{seeded[0]}/restore", null);
        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
    }

    [Fact]
    public async Task Emptying_the_trash_removes_the_rows_for_good()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Purge me");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        (await client.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync("/api/v1/trash")).EnsureSuccessStatusCode();

        Assert.Empty((await TrashAsync(client)).Playlists);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        Assert.False(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => p.Id == playlist.Id));
        Assert.False(await db.PlaylistItems.IgnoreQueryFilters().AnyAsync(i => i.PlaylistId == playlist.Id));
    }

    [Fact]
    public async Task One_deleted_playlist_can_be_removed_for_good_and_the_rest_stay()
    {
        var client = await NewUserAsync();
        var doomed = await NewPlaylistAsync(client, "Gone for good");
        var kept = await NewPlaylistAsync(client, "Still recoverable");
        await factory.SeedEnrichedItemsAsync(doomed.Id, 2);

        (await client.DeleteAsync($"/api/v1/playlists/{doomed.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/v1/playlists/{kept.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/trash/playlists/{doomed.Id}")).StatusCode);

        var trash = await TrashAsync(client);
        Assert.DoesNotContain(trash.Playlists, p => p.Id == doomed.Id);
        Assert.Contains(trash.Playlists, p => p.Id == kept.Id);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        Assert.False(await db.PlaylistItems.IgnoreQueryFilters().AnyAsync(i => i.PlaylistId == doomed.Id));
    }

    [Fact]
    public async Task One_deleted_item_can_be_removed_for_good_but_not_a_live_one_or_a_strangers()
    {
        var client = await NewUserAsync();
        var stranger = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Mostly kept");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        (await client.DeleteAsync($"/api/v1/items/{seeded[0]}")).EnsureSuccessStatusCode();

        // A live item is not the trash's to remove, and nobody else's trash is yours.
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/trash/items/{seeded[1]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/trash/items/{seeded[0]}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/trash/items/{seeded[0]}")).StatusCode);
        Assert.DoesNotContain((await TrashAsync(client)).Items, i => i.Id == seeded[0]);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        Assert.False(await db.PlaylistItems.IgnoreQueryFilters().AnyAsync(i => i.Id == seeded[0]));
        Assert.True(await db.PlaylistItems.AnyAsync(i => i.Id == seeded[1]));
    }

    [Fact]
    public async Task The_purge_job_only_takes_rows_past_the_retention_window()
    {
        var client = await NewUserAsync();
        var recent = await NewPlaylistAsync(client, "Deleted just now");
        var old = await NewPlaylistAsync(client, "Deleted long ago");

        (await client.DeleteAsync($"/api/v1/playlists/{recent.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/v1/playlists/{old.Id}")).EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var stale = await db.Playlists.IgnoreQueryFilters().FirstAsync(p => p.Id == old.Id);
            stale.DeletionTime = DateTimeOffset.UtcNow.AddDays(-(TrashService.RetentionDays + 1));
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITrashService>().PurgeExpiredAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            Assert.True(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => p.Id == recent.Id));
            Assert.False(await db.Playlists.IgnoreQueryFilters().AnyAsync(p => p.Id == old.Id));
        }

        // Still restorable, because the purge left it alone.
        (await client.PostAsync($"/api/v1/trash/playlists/{recent.Id}/restore", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task One_user_cannot_see_or_restore_the_trash_of_another()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();

        var playlist = await NewPlaylistAsync(owner, "Private grief");
        (await owner.DeleteAsync($"/api/v1/playlists/{playlist.Id}")).EnsureSuccessStatusCode();

        Assert.Empty((await TrashAsync(stranger)).Playlists);

        var restore = await stranger.PostAsync($"/api/v1/trash/playlists/{playlist.Id}/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }
}
