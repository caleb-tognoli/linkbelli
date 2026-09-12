using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Saving a link by emailing it. The address is not a secret the way a POST token is — it ends up
/// in headers, in forwards, in other people's sent folders — so most of what is worth testing
/// here is who gets refused.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class EmailIngestTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name);

    private record SourceWithConfigDto(
        Guid Id, string Name, Dictionary<string, string> Config, string? WebhookToken);

    private record PushResultDto(int Received, int Found, int Added, int Skipped);

    private readonly string _run = Guid.NewGuid().ToString("N")[..8];

    private async Task<(HttpClient Client, string Username, string Email)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));

        return (client, username, $"{username}@example.com");
    }

    /// <summary>A webhook source, which is what an inbox address addresses.</summary>
    private static async Task<(Guid Id, string Token)> NewInboxSourceAsync(
        HttpClient client, Guid playlistId, Dictionary<string, string>? extraConfig = null)
    {
        var config = new Dictionary<string, string>(extraConfig ?? []);

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Inbox {Guid.NewGuid():N}",
            type = "Webhook",
            config,
            schedule = "",
            playlistIds = new[] { playlistId },
        });
        res.EnsureSuccessStatusCode();

        var created = (await res.Content.ReadFromJsonAsync<SourceWithConfigDto>())!;

        // Handed back on its own rather than in the config, which redacts it like every other
        // source secret — so the create response is the one chance to read it.
        return (created.Id, created.WebhookToken!);
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(
            "/api/v1/playlists", new { name = $"Inbox target {Guid.NewGuid():N}", visibility = "Private" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    private async Task<HttpResponseMessage> MailAsync(
        string token, string from, string? subject = null, string? text = null) =>
        await factory.CreateClient().PostAsJsonAsync(
            $"/api/v1/inbox/{token}", new { from, subject, text });

    /// <summary>
    /// Items read straight from the table, because a link is only listed once enriched and
    /// enrichment needs a live fetch these tests deliberately never make.
    /// </summary>
    private async Task<int> ItemCountAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.PlaylistItems.CountAsync(i => i.PlaylistId == playlistId);
    }

    [Fact]
    public async Task A_link_in_the_body_is_saved()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        var res = await MailAsync(
            token, email, "Worth reading", $"Have a look at https://example.com/{_run}/a");

        res.EnsureSuccessStatusCode();
        var result = (await res.Content.ReadFromJsonAsync<PushResultDto>())!;
        Assert.Equal(1, result.Received);
        Assert.Equal(1, await ItemCountAsync(playlist.Id));
    }

    [Fact]
    public async Task A_link_in_the_subject_is_saved_too()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        // Forwarding an article with its address in the subject is a normal thing to do.
        (await MailAsync(token, email, $"https://example.com/{_run}/subject", "")).EnsureSuccessStatusCode();

        Assert.Equal(1, await ItemCountAsync(playlist.Id));
    }

    [Fact]
    public async Task Several_links_in_one_message_all_arrive()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        var body = $"https://example.com/{_run}/1 and https://example.com/{_run}/2 and https://example.com/{_run}/3";
        var res = await MailAsync(token, email, "Three things", body);

        res.EnsureSuccessStatusCode();
        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<PushResultDto>())!.Received);
    }

    [Fact]
    public async Task The_same_link_twice_in_one_message_is_sent_once()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        // A quoted reply repeats every address in the message it is replying to.
        var url = $"https://example.com/{_run}/repeated";
        var res = await MailAsync(token, email, url, $"{url} and again {url}");

        res.EnsureSuccessStatusCode();
        Assert.Equal(1, (await res.Content.ReadFromJsonAsync<PushResultDto>())!.Received);
    }

    [Fact]
    public async Task A_message_with_no_links_is_refused_and_says_why()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        var res = await MailAsync(token, email, "Just saying hello", "No addresses in here at all.");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("no links", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_stranger_cannot_mail_somebody_elses_inbox()
    {
        var (client, _, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        var res = await MailAsync(
            token, "stranger@elsewhere.example", "Spam", $"https://example.com/{_run}/spam");

        // The address is not a secret the way a token is, so a leaked one must not let anybody
        // write into somebody's playlist. Refused as "not found" rather than "not allowed", so
        // trying cannot confirm the address exists.
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal(0, await ItemCountAsync(playlist.Id));
    }

    [Fact]
    public async Task A_sender_the_owner_listed_is_allowed()
    {
        var (client, _, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(
            client, playlist.Id,
            new Dictionary<string, string> { ["allowedSenders"] = "friend@elsewhere.example, other@x.example" });

        (await MailAsync(
            token, "friend@elsewhere.example", "From a friend", $"https://example.com/{_run}/friend"))
            .EnsureSuccessStatusCode();

        Assert.Equal(1, await ItemCountAsync(playlist.Id));
    }

    [Fact]
    public async Task Listing_senders_replaces_the_owner_rather_than_adding_to_them()
    {
        var (client, _, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(
            client, playlist.Id,
            new Dictionary<string, string> { ["allowedSenders"] = "only@elsewhere.example" });

        // An explicit list is a statement about who may send, and silently keeping the owner on
        // it would make the setting mean something other than what it says.
        var res = await MailAsync(token, email, "Mine", $"https://example.com/{_run}/mine");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_display_name_in_front_of_the_address_changes_nothing()
    {
        var (client, username, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        // Every mail client disagrees about whether to send the bare address or a display name.
        (await MailAsync(
            token, $"{username} <{email}>", "Named", $"https://example.com/{_run}/named"))
            .EnsureSuccessStatusCode();

        Assert.Equal(1, await ItemCountAsync(playlist.Id));
    }

    [Fact]
    public async Task A_token_nobody_has_is_refused()
    {
        var res = await MailAsync(
            "not-a-real-token", "a@b.example", "Hello", "https://example.com/nope");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_message_with_no_sender_is_refused()
    {
        var (client, _, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var (_, token) = await NewInboxSourceAsync(client, playlist.Id);

        var res = await MailAsync(token, "", "Hello", $"https://example.com/{_run}/anon");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task The_subject_titles_a_lone_link_but_not_a_list_of_them()
    {
        var (client, _, email) = await NewUserAsync();

        var one = await NewPlaylistAsync(client);
        var (_, oneToken) = await NewInboxSourceAsync(client, one.Id);
        (await MailAsync(oneToken, email, "The only one", $"https://example.com/{_run}/solo"))
            .EnsureSuccessStatusCode();

        var many = await NewPlaylistAsync(client);
        var (_, manyToken) = await NewInboxSourceAsync(client, many.Id);
        (await MailAsync(
            manyToken, email, "A few of them",
            $"https://example.com/{_run}/m1 https://example.com/{_run}/m2")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // The link's own title, which is where a discovered title goes until enrichment reads a
        // better one off the page itself.
        var solo = await db.PlaylistItems
            .Where(i => i.PlaylistId == one.Id)
            .Select(i => i.Link!.Title)
            .ToListAsync();

        var listed = await db.PlaylistItems
            .Where(i => i.PlaylistId == many.Id)
            .Select(i => i.Link!.Title)
            .ToListAsync();

        Assert.Equal("The only one", Assert.Single(solo));
        // On a message with several links the subject describes the message, not any one of
        // them, so stamping it on all of them would be wrong about every single one.
        Assert.All(listed, title => Assert.Null(title));
    }
}
