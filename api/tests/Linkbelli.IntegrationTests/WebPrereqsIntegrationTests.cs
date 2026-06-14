using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>Authenticated bearer client for a fresh user.</summary>
file static class Bearer
{
    public static async Task<HttpClient> NewUserAsync(PostgresApiFactory factory, string username)
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[Collection(IntegrationCollection.Name)]
public class DiscoveryTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Lists_public_playlists_filtered_by_query_and_tag_excluding_private()
    {
        var username = NewUsername();
        var owner = await Bearer.NewUserAsync(factory, username);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var tag = $"food{marker}";

        await owner.PostAsJsonAsync("/api/v1/playlists",
            new { name = $"Cooking {marker}", visibility = "Public", tags = new[] { tag } });
        await owner.PostAsJsonAsync("/api/v1/playlists",
            new { name = $"Secret {marker}", visibility = "Private", tags = new[] { tag } });

        var anon = factory.CreateClient();

        var byName = await anon.GetFromJsonAsync<PagedDto<PublicSummaryDto>>($"/api/v1/public/playlists?q=cooking {marker}");
        Assert.Contains(byName!.Items, p => p.Name == $"Cooking {marker}" && p.OwnerUsername == username);
        Assert.DoesNotContain(byName.Items, p => p.Name == $"Secret {marker}");

        var byTag = await anon.GetFromJsonAsync<PagedDto<PublicSummaryDto>>($"/api/v1/public/playlists?tag={tag}");
        Assert.Single(byTag!.Items); // only the Public one carries the (unique) tag
        Assert.Equal($"Cooking {marker}", byTag.Items[0].Name);
    }
}

[Collection(IntegrationCollection.Name)]
public class LinkPreviewTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Preview_of_unreachable_url_returns_canonical_url_with_no_metadata()
    {
        var client = await Bearer.NewUserAsync(factory, NewUsername());

        // Loopback is SSRF-blocked → best-effort preview returns the canonical URL, no metadata.
        var resp = await client.PostAsJsonAsync("/api/v1/links/preview", new { url = "http://localhost/whatever" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var preview = await resp.Content.ReadFromJsonAsync<LinkPreviewDto>();
        Assert.StartsWith("http://localhost/", preview!.CanonicalUrl);
        Assert.Null(preview.Title);
    }

    [Fact]
    public async Task Preview_of_invalid_url_is_400()
    {
        var client = await Bearer.NewUserAsync(factory, NewUsername());
        var resp = await client.PostAsJsonAsync("/api/v1/links/preview", new { url = "not a url" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

[Collection(IntegrationCollection.Name)]
public class RecentOrderingTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Adding_an_item_bumps_a_playlist_above_newer_empty_ones()
    {
        var client = await Bearer.NewUserAsync(factory, NewUsername());

        var aResp = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Alpha" });
        var a = await aResp.Content.ReadFromJsonAsync<PlaylistDto>();
        var bResp = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Beta" });
        var b = await bResp.Content.ReadFromJsonAsync<PlaylistDto>();

        // B is newer, so it sorts first initially.
        var before = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>("/api/v1/playlists");
        Assert.Equal(b!.Id, before!.Items[0].Id);

        // Adding an item to A makes A the most recently updated.
        await client.PostAsJsonAsync($"/api/v1/playlists/{a!.Id}/items", new { url = "https://example.com/x" });

        var after = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>("/api/v1/playlists");
        Assert.Equal(a.Id, after!.Items[0].Id);
    }
}

[Collection(IntegrationCollection.Name)]
public class TaggingTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Tags_are_normalized_searchable_and_listed()
    {
        var client = await Bearer.NewUserAsync(factory, NewUsername());
        var marker = Guid.NewGuid().ToString("N")[..8];

        var create = await client.PostAsJsonAsync("/api/v1/playlists",
            new { name = "Tagged", tags = new[] { $"Tech{marker}", $"tech{marker}", $"  AI{marker} " } });
        var playlist = await create.Content.ReadFromJsonAsync<PlaylistDto>();
        // "Tech"/"tech" dedupe to one normalized tag; "AI" trims/lowercases.
        Assert.Equal(2, playlist!.Tags.Length);
        Assert.Contains($"tech{marker}", playlist.Tags);
        Assert.Contains($"ai{marker}", playlist.Tags);

        var byTag = await client.GetFromJsonAsync<PagedDto<PlaylistDto>>($"/api/v1/playlists?tag=tech{marker}");
        Assert.Contains(byTag!.Items, p => p.Id == playlist.Id);

        var ownTags = await client.GetFromJsonAsync<List<TagSummaryDto>>("/api/v1/tags");
        Assert.Contains(ownTags!, t => t.Name == $"tech{marker}" && t.PlaylistCount == 1);
    }
}

[Collection(IntegrationCollection.Name)]
public class SourceSubscriptionTests(PostgresApiFactory factory)
{
    private static object SourceBody(string name, string visibility) => new
    {
        name,
        type = "Rss",
        config = new { feedUrl = "https://example.com/feed.xml" },
        schedule = "*/15 * * * *",
        visibility,
    };

    [Fact]
    public async Task Owner_subscribes_own_source_others_only_if_shared()
    {
        var aName = NewUsername();
        var userA = await Bearer.NewUserAsync(factory, aName);

        var sharedResp = await userA.PostAsJsonAsync("/api/v1/sources", SourceBody($"Shared {aName}", "Shared"));
        var shared = await sharedResp.Content.ReadFromJsonAsync<SourceDto>();
        var privateResp = await userA.PostAsJsonAsync("/api/v1/sources", SourceBody($"Private {aName}", "Private"));
        var priv = await privateResp.Content.ReadFromJsonAsync<SourceDto>();
        Assert.Equal("Shared", shared!.Visibility);
        Assert.Equal("Private", priv!.Visibility);

        // Owner attaches their own (private) source to their own playlist.
        var paResp = await userA.PostAsJsonAsync("/api/v1/playlists", new { name = "A list" });
        var pa = await paResp.Content.ReadFromJsonAsync<PlaylistDto>();
        var ownSub = await userA.PostAsJsonAsync($"/api/v1/playlists/{pa!.Id}/sources", new { sourceId = priv.Id });
        Assert.Equal(HttpStatusCode.NoContent, ownSub.StatusCode);

        // User B can subscribe the SHARED source but not the PRIVATE one.
        var userB = await Bearer.NewUserAsync(factory, NewUsername());
        var pbResp = await userB.PostAsJsonAsync("/api/v1/playlists", new { name = "B list" });
        var pb = await pbResp.Content.ReadFromJsonAsync<PlaylistDto>();

        var subShared = await userB.PostAsJsonAsync($"/api/v1/playlists/{pb!.Id}/sources", new { sourceId = shared.Id });
        Assert.Equal(HttpStatusCode.NoContent, subShared.StatusCode);

        var subPrivate = await userB.PostAsJsonAsync($"/api/v1/playlists/{pb.Id}/sources", new { sourceId = priv.Id });
        Assert.Equal(HttpStatusCode.NotFound, subPrivate.StatusCode);

        // B can discover the shared source (and not the private one).
        var sharedList = await userB.GetFromJsonAsync<List<SharedSourceDto>>("/api/v1/sources/shared");
        Assert.Contains(sharedList!, s => s.Id == shared.Id && s.OwnerUsername == aName);
        Assert.DoesNotContain(sharedList!, s => s.Id == priv.Id);

        // B sees the shared source attached to its playlist (not owned by B).
        var attached = await userB.GetFromJsonAsync<List<AttachedSourceDto>>($"/api/v1/playlists/{pb.Id}/sources");
        Assert.Contains(attached!, s => s.Id == shared.Id && !s.OwnedByMe && s.OwnerUsername == aName);

        // Unsubscribe is idempotent and succeeds, and the source is gone from the list.
        var unsub = await userB.DeleteAsync($"/api/v1/playlists/{pb.Id}/sources/{shared.Id}");
        Assert.Equal(HttpStatusCode.NoContent, unsub.StatusCode);
        var afterUnsub = await userB.GetFromJsonAsync<List<AttachedSourceDto>>($"/api/v1/playlists/{pb.Id}/sources");
        Assert.DoesNotContain(afterUnsub!, s => s.Id == shared.Id);
    }
}
