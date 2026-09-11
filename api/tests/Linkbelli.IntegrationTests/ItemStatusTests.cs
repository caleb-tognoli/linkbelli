using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Status now records <em>when</em> it changed, which is what makes "what did I get through this
/// week" answerable at all.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ItemStatusTests(PostgresApiFactory factory)
{
    private record ItemStatusDto(Guid Id, string Status, DateTimeOffset? StatusChangedAt);
    private record HitDto(Guid ItemId, string Status, DateTimeOffset? StatusChangedAt);
    private record SearchPageDto(List<HitDto> Items, string? NextCursor, int? Total);

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

    [Fact]
    public async Task A_fresh_item_has_never_changed_status()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Untouched");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var note = await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { note = "just a note" });
        note.EnsureSuccessStatusCode();
        var item = (await note.Content.ReadFromJsonAsync<ItemStatusDto>())!;

        Assert.Equal("Added", item.Status);
        Assert.Null(item.StatusChangedAt);
    }

    [Fact]
    public async Task Marking_an_item_watched_stamps_when()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Finished");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var res = await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { status = "Watched" });
        res.EnsureSuccessStatusCode();
        var item = (await res.Content.ReadFromJsonAsync<ItemStatusDto>())!;

        Assert.Equal("Watched", item.Status);
        Assert.NotNull(item.StatusChangedAt);
        Assert.InRange(item.StatusChangedAt!.Value, before, DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task Re_sending_the_same_status_does_not_move_the_timestamp()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Idempotent status");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 1);

        var first = await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { status = "Watched" });
        first.EnsureSuccessStatusCode();
        var stamped = (await first.Content.ReadFromJsonAsync<ItemStatusDto>())!.StatusChangedAt;

        await Task.Delay(20);

        var again = await client.PatchAsJsonAsync($"/api/v1/items/{seeded[0]}", new { status = "Watched" });
        again.EnsureSuccessStatusCode();
        var unchanged = (await again.Content.ReadFromJsonAsync<ItemStatusDto>())!.StatusChangedAt;

        // Nothing changed, so nothing happened — re-marking should not look like fresh progress.
        Assert.Equal(stamped, unchanged);
    }

    [Fact]
    public async Task Search_can_ask_what_was_finished_recently()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "This week");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        foreach (var id in seeded)
        {
            (await client.PatchAsJsonAsync($"/api/v1/items/{id}", new { status = "Watched" }))
                .EnsureSuccessStatusCode();
        }

        // Age one of them out of the window.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var old = await db.PlaylistItems.FirstAsync(i => i.Id == seeded[0]);
            old.StatusChangedAt = DateTimeOffset.UtcNow.AddDays(-30);
            await db.SaveChangesAsync();
        }

        var since = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"));
        var res = await client.GetAsync($"/api/v1/search?finishedSince={since}");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<SearchPageDto>())!;

        Assert.Equal(2, page.Total);
        Assert.DoesNotContain(page.Items, h => h.ItemId == seeded[0]);
    }

    [Fact]
    public async Task An_item_never_marked_watched_is_not_counted_as_finished()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Never finished");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var since = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"));
        var res = await client.GetAsync($"/api/v1/search?finishedSince={since}");
        res.EnsureSuccessStatusCode();

        Assert.Equal(0, (await res.Content.ReadFromJsonAsync<SearchPageDto>())!.Total);
    }
}
