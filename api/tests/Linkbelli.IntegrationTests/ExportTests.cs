using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Data portability. The important guarantees: a user gets their own data and nobody else's, and
/// an exported source never carries the secrets in its config.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ExportTests(PostgresApiFactory factory)
{
    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        var token = await client.RegisterAndLoginAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, username);
    }

    private static async Task<PlaylistDto> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, tags = new[] { "tech" } });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    [Fact]
    public async Task Json_export_carries_the_callers_playlists_and_items()
    {
        var (client, username) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Exportable");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3, title: n => $"Post {n}");

        var res = await client.GetAsync("/api/v1/export?format=json");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(username, doc.RootElement.GetProperty("username").GetString());

        var exported = doc.RootElement.GetProperty("playlists")
            .EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == playlist.Id.ToString());

        Assert.Equal("Exportable", exported.GetProperty("name").GetString());
        Assert.Equal(3, exported.GetProperty("items").GetArrayLength());
        Assert.Equal("tech", exported.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task An_export_is_served_as_a_dated_attachment()
    {
        var (client, _) = await NewUserAsync();
        await NewPlaylistAsync(client, "Attachment");

        var res = await client.GetAsync("/api/v1/export?format=csv");
        res.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", res.Content.Headers.ContentType!.MediaType);
        var disposition = res.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.EndsWith(".csv", disposition.FileName!.Trim('"'));
    }

    [Fact]
    public async Task Csv_export_feeds_straight_back_into_the_importer()
    {
        var (client, _) = await NewUserAsync();
        var source = await NewPlaylistAsync(client, "Round trip source");
        await factory.SeedEnrichedItemsAsync(source.Id, 2);

        var csv = await (await client.GetAsync($"/api/v1/export/playlists/{source.Id}?format=csv"))
            .Content.ReadAsStringAsync();

        var rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => new { url = line.Split(',')[0], note = (string?)null })
            .ToArray();
        Assert.Equal(2, rows.Length);

        // Import the exported rows into a fresh playlist: they are accepted as valid URLs.
        var target = await NewPlaylistAsync(client, "Round trip target");
        var import = await client.PostAsJsonAsync("/api/v1/import", new { rows, playlistId = target.Id });
        import.EnsureSuccessStatusCode();

        using var result = JsonDocument.Parse(await import.Content.ReadAsStringAsync());
        Assert.Equal(2, result.RootElement.GetProperty("imported").GetInt32());
        Assert.Empty(result.RootElement.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Html_export_is_a_browser_importable_bookmark_file()
    {
        var (client, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Bookmarks");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1, title: _ => "Saved page");

        var res = await client.GetAsync("/api/v1/export?format=html");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/html", res.Content.Headers.ContentType!.MediaType);

        var html = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("<!DOCTYPE NETSCAPE-Bookmark-file-1>", html);
        Assert.Contains("Bookmarks</H3>", html);
        Assert.Contains("Saved page</A>", html);
    }

    [Fact]
    public async Task Opml_export_lists_the_callers_feed_sources()
    {
        var (client, _) = await NewUserAsync();

        var created = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Exported feed",
            type = "Rss",
            config = new { feedUrl = "https://export.example/feed.xml" },
            schedule = "0 * * * *",
        });
        created.EnsureSuccessStatusCode();

        var res = await client.GetAsync("/api/v1/export?format=opml");
        res.EnsureSuccessStatusCode();
        Assert.Equal("text/x-opml", res.Content.Headers.ContentType!.MediaType);

        var outlines = XDocument.Parse(await res.Content.ReadAsStringAsync())
            .Root!.Element("body")!.Elements("outline").ToList();

        var outline = Assert.Single(outlines);
        Assert.Equal("Exported feed", outline.Attribute("text")!.Value);
        Assert.Equal("https://export.example/feed.xml", outline.Attribute("xmlUrl")!.Value);
    }

    [Fact]
    public async Task Source_secrets_are_redacted_in_an_export()
    {
        var (client, _) = await NewUserAsync();

        var created = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Authenticated API",
            type = "JsonApi",
            config = new Dictionary<string, string>
            {
                ["url"] = "https://api.example/v1/posts",
                ["itemsPath"] = "$.posts[*]",
                ["urlPath"] = "permalink",
                ["header.Authorization"] = "Bearer super-secret-token",
            },
            schedule = "0 * * * *",
        });
        created.EnsureSuccessStatusCode();

        var json = await (await client.GetAsync("/api/v1/export?format=json")).Content.ReadAsStringAsync();

        // An export is a file that gets emailed around; the token must not be in it.
        Assert.DoesNotContain("super-secret-token", json);
        Assert.Contains("***", json);
    }

    [Fact]
    public async Task A_single_playlist_can_be_exported_on_its_own()
    {
        var (client, _) = await NewUserAsync();
        var wanted = await NewPlaylistAsync(client, "Just this one");
        var other = await NewPlaylistAsync(client, "Not this one");
        await factory.SeedEnrichedItemsAsync(wanted.Id, 2);
        await factory.SeedEnrichedItemsAsync(other.Id, 2);

        var res = await client.GetAsync($"/api/v1/export/playlists/{wanted.Id}?format=json");
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var playlist = Assert.Single(doc.RootElement.GetProperty("playlists").EnumerateArray());
        Assert.Equal("Just this one", playlist.GetProperty("name").GetString());
    }

    [Fact]
    public async Task One_user_cannot_export_anothers_playlist()
    {
        var (owner, _) = await NewUserAsync();
        var (stranger, _) = await NewUserAsync();

        var playlist = await NewPlaylistAsync(owner, "Not yours");

        var res = await stranger.GetAsync($"/api/v1/export/playlists/{playlist.Id}?format=json");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task An_export_never_includes_another_users_data()
    {
        var (owner, _) = await NewUserAsync();
        var (stranger, _) = await NewUserAsync();

        await NewPlaylistAsync(owner, "Mine alone");
        await NewPlaylistAsync(stranger, "Theirs alone");

        var json = await (await owner.GetAsync("/api/v1/export?format=json")).Content.ReadAsStringAsync();

        Assert.Contains("Mine alone", json);
        Assert.DoesNotContain("Theirs alone", json);
    }

    [Fact]
    public async Task An_unknown_format_is_rejected_with_a_usable_message()
    {
        var (client, _) = await NewUserAsync();

        var res = await client.GetAsync("/api/v1/export?format=pdf");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("json, csv, html, opml", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task No_format_defaults_to_json()
    {
        var (client, _) = await NewUserAsync();
        await NewPlaylistAsync(client, "Default format");

        var res = await client.GetAsync("/api/v1/export");
        res.EnsureSuccessStatusCode();

        Assert.Equal("application/json", res.Content.Headers.ContentType!.MediaType);
    }
}
