using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The site favicon is stored once per Host and surfaced on every link there, so the item list
/// has a visual anchor for pages that carry no image of their own.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class HostBrandingTests(PostgresApiFactory factory)
{
    private record LinkDto(Guid Id, string Url, string Host, string? Title, string? Description,
        string? ThumbnailUrl, string? SiteName, bool Enriched, bool Nsfw, string? Favicon);

    private record ItemWithLinkDto(Guid Id, LinkDto Link);

    private record PagedDto(List<ItemWithLinkDto> Items, string? NextCursor, int? Total);

    [Fact]
    public async Task A_hosts_favicon_is_returned_on_every_link_from_that_site()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Branding" });
        created.EnsureSuccessStatusCode();
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        await factory.SeedEnrichedItemsAsync(playlist, 2);

        // Enrichment stamps the host row; do the same directly so the assertion doesn't need a fetch.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
            var host = await db.Hosts.FirstAsync(h => h.Hostname == "seed.example");
            host.Favicon = "https://seed.example/favicon.ico";
            host.DisplayName = "Seed Example";
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync($"/api/v1/playlists/{playlist}/items");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<PagedDto>())!;

        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.Equal("https://seed.example/favicon.ico", i.Link.Favicon));
    }

    [Fact]
    public async Task A_link_on_a_site_we_have_never_enriched_reports_no_favicon()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "No branding" });
        created.EnsureSuccessStatusCode();
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;

        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(playlist, 1, url: _ => $"https://blank-{tag}.example/a");

        var res = await client.GetAsync($"/api/v1/playlists/{playlist}/items");
        res.EnsureSuccessStatusCode();
        var page = (await res.Content.ReadFromJsonAsync<PagedDto>())!;

        Assert.Single(page.Items);
        Assert.Null(page.Items[0].Link.Favicon);
    }
}
