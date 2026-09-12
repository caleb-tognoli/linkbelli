using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Linkbelli.Application.Email;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The mail Linkbelli sends without being asked. These cover who gets it, who does not, and the
/// unsubscribe link — which has to work for somebody who cannot or will not sign in, because
/// that is the person most likely to be clicking it.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class NotificationTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name);

    private record PrefsDto(bool OnShare, bool OnFollow, bool OnSourceStopped, bool WeeklyDigest);

    private async Task<(HttpClient Client, string Username, string Email)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));

        return (client, username, $"{username}@example.com");
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(
        HttpClient client, string name, string visibility = "Private")
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    /// <summary>
    /// Notifications go through Hangfire, so the enqueued job has to be run for the mail to
    /// exist. Invoked directly rather than waiting on a worker, which would make every one of
    /// these tests a race.
    /// </summary>
    private async Task RunShareAsync(Guid recipientId, Guid playlistId, Guid actorId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .SendShareAsync(recipientId, playlistId, actorId);
    }

    private async Task<Guid> UserIdAsync(string username)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Application.Data.IAppDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstAsync(db.Users.Where(u => u.UserName == username).Select(u => u.Id));
    }

    private static string TokenFrom(string body)
    {
        var match = Regex.Match(body, @"unsubscribe\?token=(\S+)");
        Assert.True(match.Success, $"No unsubscribe link in:\n{body}");

        return Uri.UnescapeDataString(match.Groups[1].Value);
    }

    private async Task<HttpResponseMessage> UnsubscribeAsync(string token) =>
        await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/notifications/unsubscribe", new { token });

    [Fact]
    public async Task Sharing_a_playlist_tells_the_person_it_was_shared_with()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (_, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");

        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Contributor" })).EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));

        var mail = factory.Email.LastTo(recipientEmail);
        Assert.NotNull(mail);
        Assert.Contains(ownerName, mail!.Subject);
        Assert.Contains(playlist.Name, mail.Subject);
        // What they can do with it, which is the question somebody actually has.
        Assert.Contains("You can add links to it.", mail.TextBody);
    }

    [Fact]
    public async Task Every_notification_carries_its_own_way_out()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (_, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));

        var mail = factory.Email.LastTo(recipientEmail)!;
        Assert.Contains("/unsubscribe?token=", mail.TextBody);
        Assert.Contains("Stop these", mail.TextBody);
    }

    [Fact]
    public async Task The_unsubscribe_link_works_without_signing_in()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (recipient, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));
        var token = TokenFrom(factory.Email.LastTo(recipientEmail)!.TextBody);

        // A brand-new client with no session at all, which is what a mail client is.
        var res = await UnsubscribeAsync(token);

        res.EnsureSuccessStatusCode();
        var prefs = await recipient.GetFromJsonAsync<PrefsDto>("/api/v1/notifications");
        Assert.False(prefs!.OnShare);
    }

    [Fact]
    public async Task Unsubscribing_turns_off_only_that_one_kind()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (recipient, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();
        (await recipient.PutAsJsonAsync("/api/v1/notifications", new { onFollow = true }))
            .EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));
        (await UnsubscribeAsync(TokenFrom(factory.Email.LastTo(recipientEmail)!.TextBody)))
            .EnsureSuccessStatusCode();

        var prefs = await recipient.GetFromJsonAsync<PrefsDto>("/api/v1/notifications");
        Assert.False(prefs!.OnShare);
        // One link should not silence everything; somebody unsubscribing from shares has said
        // nothing about anything else.
        Assert.True(prefs.OnFollow);
        Assert.True(prefs.OnSourceStopped);
    }

    [Fact]
    public async Task Clicking_the_same_link_twice_is_not_an_error()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (_, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));
        var token = TokenFrom(factory.Email.LastTo(recipientEmail)!.TextBody);

        // Mail clients prefetch links, and people click twice when nothing appears to happen.
        (await UnsubscribeAsync(token)).EnsureSuccessStatusCode();
        (await UnsubscribeAsync(token)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_forged_unsubscribe_token_does_nothing()
    {
        var res = await UnsubscribeAsync("not-a-real-protected-token");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Somebody_who_asked_not_to_hear_about_shares_does_not()
    {
        var (owner, ownerName, _) = await NewUserAsync();
        var (recipient, recipientName, recipientEmail) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();

        (await recipient.PutAsJsonAsync("/api/v1/notifications", new { onShare = false }))
            .EnsureSuccessStatusCode();

        factory.Email.Clear();
        await RunShareAsync(await UserIdAsync(recipientName), playlist.Id, await UserIdAsync(ownerName));

        Assert.Null(factory.Email.LastTo(recipientEmail));
    }

    [Fact]
    public async Task The_defaults_are_the_rare_ones_only()
    {
        var (client, _, _) = await NewUserAsync();

        var prefs = await client.GetFromJsonAsync<PrefsDto>("/api/v1/notifications");

        // Rare and caused by something done to your account: on. Recurring or somebody else's
        // activity: chosen, not discovered.
        Assert.True(prefs!.OnShare);
        Assert.True(prefs.OnSourceStopped);
        Assert.False(prefs.OnFollow);
        Assert.False(prefs.WeeklyDigest);
    }

    [Fact]
    public async Task Saving_one_switch_leaves_the_others_alone()
    {
        var (client, _, _) = await NewUserAsync();

        (await client.PutAsJsonAsync("/api/v1/notifications", new { weeklyDigest = true }))
            .EnsureSuccessStatusCode();

        var prefs = await client.GetFromJsonAsync<PrefsDto>("/api/v1/notifications");
        Assert.True(prefs!.WeeklyDigest);
        Assert.True(prefs.OnShare);
        Assert.False(prefs.OnFollow);
    }

    [Fact]
    public async Task Somebody_elses_preferences_are_not_reachable()
    {
        var (first, _, _) = await NewUserAsync();
        (await first.PutAsJsonAsync("/api/v1/notifications", new { onShare = false }))
            .EnsureSuccessStatusCode();

        var (second, _, _) = await NewUserAsync();
        var prefs = await second.GetFromJsonAsync<PrefsDto>("/api/v1/notifications");

        Assert.True(prefs!.OnShare);
    }

    [Fact]
    public async Task Preferences_need_a_session()
    {
        var anonymous = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/notifications")).StatusCode);
    }

    [Fact]
    public async Task A_re_share_does_not_send_a_second_notification()
    {
        var (owner, _, _) = await NewUserAsync();
        var (_, recipientName, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");

        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Viewer" })).EnsureSuccessStatusCode();

        factory.Email.Clear();

        // Changing a role is not news, and the endpoint is the same one.
        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist.Id}/members/{recipientName}",
            new { role = "Editor" })).EnsureSuccessStatusCode();

        Assert.Empty(factory.Email.Sent);
    }
}
