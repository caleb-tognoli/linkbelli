using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What a brand-new account is told to do next. The checklist reads the account's real contents
/// rather than a stored flag, so these cover the numbers it reads and the one-way dismissal —
/// which has to survive moving to another device, and must not be undoable by accident.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class OnboardingTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(
        HttpClient client, string name, string visibility = "Private")
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    private static async Task<JsonElement> UsageAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/v1/me/usage");

    private static async Task<JsonElement> MeAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>("/api/v1/me");

    [Fact]
    public async Task A_new_account_reports_nothing_to_show_for_itself()
    {
        var user = await NewUserAsync();

        var usage = await UsageAsync(user);

        // Every step of the checklist reads one of these, so all of them starting at zero is
        // what makes the checklist appear at all.
        Assert.Equal(0, usage.GetProperty("playlists").GetInt32());
        Assert.Equal(0, usage.GetProperty("items").GetInt32());
        Assert.Equal(0, usage.GetProperty("sources").GetInt32());
        Assert.Equal(0, usage.GetProperty("published").GetInt32());
    }

    [Fact]
    public async Task Making_a_playlist_shows_up_immediately()
    {
        var user = await NewUserAsync();
        await NewPlaylistAsync(user, $"First {Guid.NewGuid():N}");

        Assert.Equal(1, (await UsageAsync(user)).GetProperty("playlists").GetInt32());
    }

    [Fact]
    public async Task A_link_that_has_not_been_fetched_yet_still_counts_as_added()
    {
        var user = await NewUserAsync();
        var playlist = await NewPlaylistAsync(user, $"Pending {Guid.NewGuid():N}");

        (await user.PostAsJsonAsync($"/api/v1/playlists/{playlist.Id}/items",
            new { url = $"https://example.com/onboard-{Guid.NewGuid():N}" })).EnsureSuccessStatusCode();

        var usage = await UsageAsync(user);

        // The step asked the person to add a link; waiting on the fetch afterwards is our job,
        // not a reason to tell them they haven't done it.
        Assert.True(
            usage.GetProperty("items").GetInt32() + usage.GetProperty("pendingItems").GetInt32() > 0);
    }

    [Fact]
    public async Task Only_playlists_anyone_else_can_see_count_as_published()
    {
        var user = await NewUserAsync();
        await NewPlaylistAsync(user, $"Mine {Guid.NewGuid():N}");
        Assert.Equal(0, (await UsageAsync(user)).GetProperty("published").GetInt32());

        await NewPlaylistAsync(user, $"Unlisted {Guid.NewGuid():N}", "Unlisted");
        await NewPlaylistAsync(user, $"Public {Guid.NewGuid():N}", "Public");

        // Unlisted counts: a link somebody can hand out is not private, whatever it is called.
        Assert.Equal(2, (await UsageAsync(user)).GetProperty("published").GetInt32());
    }

    [Fact]
    public async Task The_checklist_starts_undismissed()
    {
        var user = await NewUserAsync();

        Assert.False((await MeAsync(user)).GetProperty("onboardingDismissed").GetBoolean());
    }

    [Fact]
    public async Task Putting_it_away_sticks()
    {
        var user = await NewUserAsync();

        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { dismissOnboarding = true }))
            .EnsureSuccessStatusCode();

        // Read back rather than trusting the write: this is the whole point of storing it on the
        // account instead of in one browser.
        Assert.True((await MeAsync(user)).GetProperty("onboardingDismissed").GetBoolean());
    }

    [Fact]
    public async Task Saving_another_preference_does_not_bring_it_back()
    {
        var user = await NewUserAsync();
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { dismissOnboarding = true }))
            .EnsureSuccessStatusCode();

        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { showNsfw = true }))
            .EnsureSuccessStatusCode();
        // Not even an explicit false, which is a client bug rather than a request to be nagged
        // again — there is deliberately no way back through this endpoint.
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { dismissOnboarding = false }))
            .EnsureSuccessStatusCode();

        Assert.True((await MeAsync(user)).GetProperty("onboardingDismissed").GetBoolean());
    }

    [Fact]
    public async Task Dismissing_twice_is_not_an_error()
    {
        var user = await NewUserAsync();

        for (var i = 0; i < 2; i++)
        {
            (await user.PutAsJsonAsync("/api/v1/me/preferences", new { dismissOnboarding = true }))
                .EnsureSuccessStatusCode();
        }

        Assert.True((await MeAsync(user)).GetProperty("onboardingDismissed").GetBoolean());
    }

    [Fact]
    public async Task One_persons_dismissal_is_not_everybodys()
    {
        var user = await NewUserAsync();
        (await user.PutAsJsonAsync("/api/v1/me/preferences", new { dismissOnboarding = true }))
            .EnsureSuccessStatusCode();

        var other = await NewUserAsync();

        Assert.False((await MeAsync(other)).GetProperty("onboardingDismissed").GetBoolean());
    }
}
