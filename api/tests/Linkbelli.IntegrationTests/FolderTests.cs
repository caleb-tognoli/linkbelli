using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>Authenticated bearer client helper for folder tests.</summary>
file static class FolderBearer
{
    public static async Task<HttpClient> NewUserAsync(PostgresApiFactory factory, string username)
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

file record FolderDto(Guid Id, string Name, Guid? ParentId, int SubfolderCount, int PlaylistCount);

file record PlaylistWithFolderDto(Guid Id, string Name, Guid? FolderId, string? FolderName);

file record BreadcrumbDto(Guid Id, string Name);

file record FolderEntryDto(Guid PlaylistId, string Name, string Slug, string Visibility, bool OwnedByMe, string OwnerUsername);

file record FolderDetailDto(
    Guid Id, string Name, Guid? ParentId,
    List<BreadcrumbDto> Breadcrumbs, List<FolderDto> Subfolders, List<FolderEntryDto> Playlists);

[Collection(IntegrationCollection.Name)]
public class FolderOrganizationTests(PostgresApiFactory factory)
{
    [Fact]
    public async Task Creates_nested_folders_and_reports_tree_with_breadcrumbs()
    {
        var client = await FolderBearer.NewUserAsync(factory, NewUsername());

        var root = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Reading" }))
            .Content.ReadFromJsonAsync<FolderDto>();
        var child = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Tech", parentId = root!.Id }))
            .Content.ReadFromJsonAsync<FolderDto>();
        Assert.Equal(root.Id, child!.ParentId);

        // Flat list carries both, with the parent reporting one subfolder.
        var all = await client.GetFromJsonAsync<List<FolderDto>>("/api/v1/folders");
        Assert.Equal(1, all!.Single(f => f.Id == root.Id).SubfolderCount);

        // Detail of the parent lists the child as a subfolder.
        var rootDetail = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{root.Id}");
        Assert.Contains(rootDetail!.Subfolders, f => f.Id == child.Id);

        // Detail of the child carries a root-first breadcrumb to its parent.
        var childDetail = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{child.Id}");
        Assert.Single(childDetail!.Breadcrumbs);
        Assert.Equal(root.Id, childDetail.Breadcrumbs[0].Id);
    }

    [Fact]
    public async Task Files_own_playlist_then_moving_it_to_another_folder_relocates_it()
    {
        var client = await FolderBearer.NewUserAsync(factory, NewUsername());

        var a = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "A" })).Content.ReadFromJsonAsync<FolderDto>();
        var b = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "B" })).Content.ReadFromJsonAsync<FolderDto>();
        var pl = await (await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Mine" })).Content.ReadFromJsonAsync<PlaylistDto>();

        var fileIt = await client.PostAsJsonAsync($"/api/v1/folders/{a!.Id}/playlists", new { playlistId = pl!.Id });
        Assert.Equal(HttpStatusCode.NoContent, fileIt.StatusCode);

        var aDetail = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{a.Id}");
        Assert.Contains(aDetail!.Playlists, e => e.PlaylistId == pl.Id && e.OwnedByMe);

        // Filing into another folder MOVES it (one folder per playlist per user).
        await client.PostAsJsonAsync($"/api/v1/folders/{b!.Id}/playlists", new { playlistId = pl.Id });
        var aAfter = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{a.Id}");
        var bAfter = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{b.Id}");
        Assert.DoesNotContain(aAfter!.Playlists, e => e.PlaylistId == pl.Id);
        Assert.Contains(bAfter!.Playlists, e => e.PlaylistId == pl.Id);

        // Removing unfiles it.
        var remove = await client.DeleteAsync($"/api/v1/folders/{b.Id}/playlists/{pl.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var bEmpty = await client.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{b.Id}");
        Assert.Empty(bEmpty!.Playlists);
    }

    [Fact]
    public async Task Saves_another_users_public_playlist_but_not_a_private_one()
    {
        var ownerName = NewUsername();
        var owner = await FolderBearer.NewUserAsync(factory, ownerName);
        var pub = await (await owner.PostAsJsonAsync("/api/v1/playlists", new { name = "Public list", visibility = "Public" }))
            .Content.ReadFromJsonAsync<PlaylistDto>();
        var priv = await (await owner.PostAsJsonAsync("/api/v1/playlists", new { name = "Secret", visibility = "Private" }))
            .Content.ReadFromJsonAsync<PlaylistDto>();

        var saver = await FolderBearer.NewUserAsync(factory, NewUsername());
        var folder = await (await saver.PostAsJsonAsync("/api/v1/folders", new { name = "Saved" }))
            .Content.ReadFromJsonAsync<FolderDto>();

        var savePublic = await saver.PostAsJsonAsync($"/api/v1/folders/{folder!.Id}/playlists", new { playlistId = pub!.Id });
        Assert.Equal(HttpStatusCode.NoContent, savePublic.StatusCode);

        var savePrivate = await saver.PostAsJsonAsync($"/api/v1/folders/{folder.Id}/playlists", new { playlistId = priv!.Id });
        Assert.Equal(HttpStatusCode.NotFound, savePrivate.StatusCode);

        // The saved entry is flagged as not-owned and carries the original owner's username.
        var detail = await saver.GetFromJsonAsync<FolderDetailDto>($"/api/v1/folders/{folder.Id}");
        var entry = Assert.Single(detail!.Playlists);
        Assert.Equal(pub.Id, entry.PlaylistId);
        Assert.False(entry.OwnedByMe);
        Assert.Equal(ownerName, entry.OwnerUsername);
    }

    [Fact]
    public async Task Unfiled_filter_and_folder_placement_on_playlist_response()
    {
        var client = await FolderBearer.NewUserAsync(factory, NewUsername());
        var marker = Guid.NewGuid().ToString("N")[..8];

        var pl = await (await client.PostAsJsonAsync("/api/v1/playlists", new { name = $"PL {marker}" }))
            .Content.ReadFromJsonAsync<PlaylistDto>();
        var folder = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = $"F {marker}" }))
            .Content.ReadFromJsonAsync<FolderDto>();

        // While unfiled: appears in the root (?unfiled=true) view with no folder, and GET reports null.
        var rootBefore = await client.GetFromJsonAsync<PagedDto<PlaylistWithFolderDto>>("/api/v1/playlists?unfiled=true");
        Assert.Contains(rootBefore!.Items, p => p.Id == pl!.Id);
        var getBefore = await client.GetFromJsonAsync<PlaylistWithFolderDto>($"/api/v1/playlists/{pl!.Id}");
        Assert.Null(getBefore!.FolderId);

        // File it into the folder.
        await client.PostAsJsonAsync($"/api/v1/folders/{folder!.Id}/playlists", new { playlistId = pl.Id });

        // Now excluded from the root view, but still present in the full (no-filter) list.
        var rootAfter = await client.GetFromJsonAsync<PagedDto<PlaylistWithFolderDto>>("/api/v1/playlists?unfiled=true");
        Assert.DoesNotContain(rootAfter!.Items, p => p.Id == pl.Id);
        var allAfter = await client.GetFromJsonAsync<PagedDto<PlaylistWithFolderDto>>("/api/v1/playlists");
        Assert.Contains(allAfter!.Items, p => p.Id == pl.Id);

        // GET now reports the folder placement (id + name).
        var getAfter = await client.GetFromJsonAsync<PlaylistWithFolderDto>($"/api/v1/playlists/{pl.Id}");
        Assert.Equal(folder.Id, getAfter!.FolderId);
        Assert.Equal($"F {marker}", getAfter.FolderName);
    }

    [Fact]
    public async Task Folders_are_private_to_their_owner()
    {
        var a = await FolderBearer.NewUserAsync(factory, NewUsername());
        var folder = await (await a.PostAsJsonAsync("/api/v1/folders", new { name = "Mine" }))
            .Content.ReadFromJsonAsync<FolderDto>();

        var b = await FolderBearer.NewUserAsync(factory, NewUsername());
        var get = await b.GetAsync($"/api/v1/folders/{folder!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        var b2 = await b.GetFromJsonAsync<List<FolderDto>>("/api/v1/folders");
        Assert.DoesNotContain(b2!, f => f.Id == folder.Id);
    }

    [Fact]
    public async Task Cannot_move_a_folder_into_its_own_subtree()
    {
        var client = await FolderBearer.NewUserAsync(factory, NewUsername());
        var parent = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "P" })).Content.ReadFromJsonAsync<FolderDto>();
        var child = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "C", parentId = parent!.Id }))
            .Content.ReadFromJsonAsync<FolderDto>();

        // Moving the parent under its own child would form a cycle → rejected.
        var move = await client.PostAsJsonAsync($"/api/v1/folders/{parent.Id}/move", new { parentId = child!.Id });
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);

        // Moving the child to the root is fine.
        var toRoot = await client.PostAsJsonAsync($"/api/v1/folders/{child.Id}/move", new { parentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, toRoot.StatusCode);
        var moved = await toRoot.Content.ReadFromJsonAsync<FolderDto>();
        Assert.Null(moved!.ParentId);
    }

    [Fact]
    public async Task Deleting_a_folder_cascades_subfolders_and_entries_but_keeps_the_playlist()
    {
        var client = await FolderBearer.NewUserAsync(factory, NewUsername());
        var parent = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Top" })).Content.ReadFromJsonAsync<FolderDto>();
        var child = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Sub", parentId = parent!.Id }))
            .Content.ReadFromJsonAsync<FolderDto>();
        var pl = await (await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Keep me" })).Content.ReadFromJsonAsync<PlaylistDto>();
        await client.PostAsJsonAsync($"/api/v1/folders/{child!.Id}/playlists", new { playlistId = pl!.Id });

        var del = await client.DeleteAsync($"/api/v1/folders/{parent.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Both folders are gone…
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/folders/{parent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/folders/{child.Id}")).StatusCode);

        // …but the playlist itself survives and can be filed again.
        var get = await client.GetAsync($"/api/v1/playlists/{pl.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var refolder = await (await client.PostAsJsonAsync("/api/v1/folders", new { name = "Again" })).Content.ReadFromJsonAsync<FolderDto>();
        var refile = await client.PostAsJsonAsync($"/api/v1/folders/{refolder!.Id}/playlists", new { playlistId = pl.Id });
        Assert.Equal(HttpStatusCode.NoContent, refile.StatusCode);
    }
}
