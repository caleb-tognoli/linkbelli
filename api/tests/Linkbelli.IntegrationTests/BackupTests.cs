using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Export only ever helped the people who thought to run it. These cover the copy that gets made
/// on a user's behalf: that it holds their library, that an unchanged library is not stored
/// twice, that only its owner can reach it, and that deleting one really removes the bytes.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class BackupTests(PostgresApiFactory factory)
{
    private record BackupDto(
        Guid Id, DateTimeOffset TakenAt, int SizeBytes, int PlaylistCount, int ItemCount, bool Automatic);

    private record PlaylistViewDto(Guid Id, string Name);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Private" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    private static async Task<List<BackupDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<BackupDto>>("/api/v1/backups"))!;

    [Fact]
    public async Task A_new_account_has_nothing_to_restore_from()
    {
        var user = await NewUserAsync();

        Assert.Empty(await ListAsync(user));
    }

    [Fact]
    public async Task Taking_one_captures_what_the_account_holds()
    {
        var user = await NewUserAsync();
        var playlist = await NewPlaylistAsync(user, $"Backed up {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        var res = await user.PostAsync("/api/v1/backups", null);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var backup = (await res.Content.ReadFromJsonAsync<BackupDto>())!;
        Assert.Equal(1, backup.PlaylistCount);
        Assert.Equal(3, backup.ItemCount);
        Assert.False(backup.Automatic);
        Assert.True(backup.SizeBytes > 0);
    }

    [Fact]
    public async Task The_download_is_the_library_itself_not_a_summary_of_it()
    {
        var user = await NewUserAsync();
        var name = $"Restorable {Guid.NewGuid():N}";
        var playlist = await NewPlaylistAsync(user, name);
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);
        var created = (await (await user.PostAsync("/api/v1/backups", null))
            .Content.ReadFromJsonAsync<BackupDto>())!;

        var body = await user.GetStringAsync($"/api/v1/backups/{created.Id}");

        // Parsed rather than string-matched: a backup that is not valid JSON cannot be restored
        // from, however much of the right text it happens to contain.
        using var doc = JsonDocument.Parse(body);
        var playlists = doc.RootElement.GetProperty("playlists");
        Assert.Equal(name, playlists[0].GetProperty("name").GetString());
        Assert.Equal(2, playlists[0].GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task An_unchanged_library_is_not_stored_twice()
    {
        var user = await NewUserAsync();
        await NewPlaylistAsync(user, $"Static {Guid.NewGuid():N}");
        (await user.PostAsync("/api/v1/backups", null)).EnsureSuccessStatusCode();

        var second = await user.PostAsync("/api/v1/backups", null);

        // A dormant account would otherwise fill its whole retention with identical copies and
        // push out the one snapshot that differs.
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Single(await ListAsync(user));
    }

    [Fact]
    public async Task A_changed_library_is_stored_again()
    {
        var user = await NewUserAsync();
        await NewPlaylistAsync(user, $"First {Guid.NewGuid():N}");
        (await user.PostAsync("/api/v1/backups", null)).EnsureSuccessStatusCode();

        await NewPlaylistAsync(user, $"Second {Guid.NewGuid():N}");
        var second = await user.PostAsync("/api/v1/backups", null);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var all = await ListAsync(user);
        Assert.Equal(2, all.Count);
        // Newest first, so a person restoring reaches for the top of the list.
        Assert.True(all[0].TakenAt >= all[1].TakenAt);
        Assert.Equal(2, all[0].PlaylistCount);
    }

    [Fact]
    public async Task Only_the_newest_few_are_kept()
    {
        var user = await NewUserAsync();

        // One more than the retention, each differing so none is deduplicated away.
        for (var i = 0; i < 7; i++)
        {
            await NewPlaylistAsync(user, $"Round {i} {Guid.NewGuid():N}");
            (await user.PostAsync("/api/v1/backups", null)).EnsureSuccessStatusCode();
        }

        var all = await ListAsync(user);

        Assert.Equal(5, all.Count);
        // The survivors are the newest ones: the oldest is what a person is least likely to want.
        Assert.Equal(7, all[0].PlaylistCount);
        Assert.Equal(3, all[^1].PlaylistCount);
    }

    [Fact]
    public async Task Somebody_elses_backup_is_not_reachable()
    {
        var owner = await NewUserAsync();
        await NewPlaylistAsync(owner, $"Private {Guid.NewGuid():N}");
        var created = (await (await owner.PostAsync("/api/v1/backups", null))
            .Content.ReadFromJsonAsync<BackupDto>())!;

        var stranger = await NewUserAsync();

        // A backup is a copy of everything somebody owns, so this is the single most valuable
        // thing in the API to get wrong.
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/v1/backups/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.DeleteAsync($"/api/v1/backups/{created.Id}")).StatusCode);
        Assert.Empty(await ListAsync(stranger));
    }

    [Fact]
    public async Task Deleting_one_takes_it_away_for_good()
    {
        var user = await NewUserAsync();
        await NewPlaylistAsync(user, $"Doomed {Guid.NewGuid():N}");
        var created = (await (await user.PostAsync("/api/v1/backups", null))
            .Content.ReadFromJsonAsync<BackupDto>())!;

        (await user.DeleteAsync($"/api/v1/backups/{created.Id}")).EnsureSuccessStatusCode();

        Assert.Empty(await ListAsync(user));
        // Not soft-deleted and still downloadable: somebody asking to be rid of a copy of their
        // library means it, and a 200 here would mean the bytes were still sitting there.
        Assert.Equal(HttpStatusCode.NotFound,
            (await user.GetAsync($"/api/v1/backups/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Backups_are_on_unless_a_person_turns_them_off()
    {
        var user = await NewUserAsync();

        var before = await user.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(before.GetProperty("backupsEnabled").GetBoolean());

        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = false, backupsEnabled = false }))
            .EnsureSuccessStatusCode();

        var after = await user.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.False(after.GetProperty("backupsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Changing_one_preference_leaves_the_other_alone()
    {
        var user = await NewUserAsync();
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = false, backupsEnabled = false }))
            .EnsureSuccessStatusCode();

        // An older client that has never heard of backups must not switch them back on by
        // omitting the field.
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true }))
            .EnsureSuccessStatusCode();

        var after = await user.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(after.GetProperty("showNsfw").GetBoolean());
        Assert.False(after.GetProperty("backupsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Saving_one_setting_says_nothing_about_the_rest()
    {
        var user = await NewUserAsync();
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true, archiveLinks = true }))
            .EnsureSuccessStatusCode();

        // The backups panel owns one switch and sends only that one. It has no business deciding
        // what somebody chose on a different screen.
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { backupsEnabled = false }))
            .EnsureSuccessStatusCode();

        var after = await user.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(after.GetProperty("showNsfw").GetBoolean());
        Assert.True(after.GetProperty("archiveLinks").GetBoolean());
        Assert.False(after.GetProperty("backupsEnabled").GetBoolean());
    }
}
