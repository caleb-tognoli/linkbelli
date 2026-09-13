using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Inviting somebody who does not have an account yet.
/// </summary>
/// <remarks>
/// Membership resolved an existing username or failed, so collaborating meant the other person
/// already had an account here <em>and</em> you knew their exact username — which, on an instance
/// you have just stood up, nobody does. Paired with registration that can be closed, an operator's
/// only two options were "let the whole internet sign up" and "nobody can ever share with me".
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class InviteTests(PostgresApiFactory factory)
{
    private record Created(Guid Id, string Url, DateTimeOffset ExpiresAt, bool Emailed);

    private record Preview(string PlaylistName, string InvitedBy, string Role, DateTimeOffset ExpiresAt);

    private record Accepted(Guid PlaylistId, string PlaylistName, string Role);

    private record Summary(Guid Id, string? Email, string Role, DateTimeOffset ExpiresAt);

    private record MemberDto(string Username, string Role);

    private async Task<(HttpClient Client, string Username)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));
        return (client, username);
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<Created> InviteAsync(
        HttpClient client, Guid playlistId, string? email = null, string? role = null)
    {
        var res = await client.PostAsJsonAsync($"/api/v1/playlists/{playlistId}/invites", new { email, role });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Created>())!;
    }

    /// <summary>The token out of the link, the way a person's browser would take it.</summary>
    private static string TokenFrom(string url) => url[(url.LastIndexOf('/') + 1)..];

    [Fact]
    public async Task An_invitation_comes_back_as_a_link_that_can_be_looked_at_without_signing_in()
    {
        var (owner, ownerName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Ours {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist, role: "Editor");

        Assert.Contains("/invite/", invite.Url);

        // Anonymous, so somebody can see what they are being asked to join before deciding
        // whether to make an account at all.
        var preview = await factory.CreateClient()
            .GetFromJsonAsync<Preview>($"/api/v1/invites/{TokenFrom(invite.Url)}");

        Assert.Equal(ownerName, preview!.InvitedBy, ignoreCase: true);
        Assert.Equal("Editor", preview.Role);
    }

    [Fact]
    public async Task Accepting_makes_them_a_member_at_the_role_they_were_offered()
    {
        var (owner, _) = await NewUserAsync();
        var (guest, guestName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Shared {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist, role: "Contributor");

        var res = await guest.PostAsync($"/api/v1/invites/{TokenFrom(invite.Url)}/accept", null);
        res.EnsureSuccessStatusCode();
        var accepted = (await res.Content.ReadFromJsonAsync<Accepted>())!;

        Assert.Equal(playlist, accepted.PlaylistId);

        var members = await owner.GetFromJsonAsync<List<MemberDto>>($"/api/v1/playlists/{playlist}/members");
        var member = Assert.Single(members!, m => string.Equals(m.Username, guestName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Contributor", member.Role);

        // And they can actually read it, which is the whole point.
        (await guest.GetAsync($"/api/v1/playlists/{playlist}")).EnsureSuccessStatusCode();
    }

    /// <summary>One use only. A link that keeps working is a link that keeps being forwarded.</summary>
    [Fact]
    public async Task A_link_works_once()
    {
        var (owner, _) = await NewUserAsync();
        var (first, _) = await NewUserAsync();
        var (second, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Once {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);
        var token = TokenFrom(invite.Url);

        (await first.PostAsync($"/api/v1/invites/{token}/accept", null)).EnsureSuccessStatusCode();

        var again = await second.PostAsync($"/api/v1/invites/{token}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    /// <summary>
    /// Used, expired and never-existed all answer the same way. Which of the three it is tells an
    /// anonymous caller something about a link they were not given.
    /// </summary>
    [Fact]
    public async Task A_token_from_nowhere_says_the_same_thing_as_a_used_one()
    {
        var (owner, _) = await NewUserAsync();
        var (guest, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Same answer {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);
        var token = TokenFrom(invite.Url);
        (await guest.PostAsync($"/api/v1/invites/{token}/accept", null)).EnsureSuccessStatusCode();

        var used = await factory.CreateClient().GetAsync($"/api/v1/invites/{token}");
        var invented = await factory.CreateClient().GetAsync("/api/v1/invites/not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, used.StatusCode);
        Assert.Equal(used.StatusCode, invented.StatusCode);
    }

    /// <summary>
    /// Mail is optional on this product by design, so the link has to come back whether or not
    /// it was sent — otherwise an instance without mail cannot invite anybody.
    /// </summary>
    [Fact]
    public async Task The_link_comes_back_even_with_no_address_to_send_it_to()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"No mail {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);

        Assert.False(invite.Emailed);
        Assert.Contains("/invite/", invite.Url);
    }

    [Fact]
    public async Task Given_an_address_it_is_also_sent()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Posted {Guid.NewGuid():N}");
        var address = $"{NewUsername()}@example.com";

        factory.Email.Clear();
        var invite = await InviteAsync(owner, playlist, email: address);

        Assert.True(invite.Emailed);

        var mail = factory.Email.LastTo(address);
        Assert.NotNull(mail);
        Assert.Contains("/invite/", mail!.TextBody);
    }

    [Fact]
    public async Task An_owner_can_see_and_take_back_what_is_outstanding()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Outstanding {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);

        var live = await owner.GetFromJsonAsync<List<Summary>>($"/api/v1/playlists/{playlist}/invites");
        Assert.Single(live!, i => i.Id == invite.Id);

        (await owner.DeleteAsync($"/api/v1/playlists/{playlist}/invites/{invite.Id}"))
            .EnsureSuccessStatusCode();

        Assert.Empty((await owner.GetFromJsonAsync<List<Summary>>($"/api/v1/playlists/{playlist}/invites"))!);

        // And the link stops working, which is what taking it back means.
        var stranger = await NewUserAsync();
        var res = await stranger.Client.PostAsync($"/api/v1/invites/{TokenFrom(invite.Url)}/accept", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    /// A used invitation is not outstanding, and listing it would make a short list look long.
    /// </summary>
    [Fact]
    public async Task An_accepted_invitation_leaves_the_outstanding_list()
    {
        var (owner, _) = await NewUserAsync();
        var (guest, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Done with {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);
        (await guest.PostAsync($"/api/v1/invites/{TokenFrom(invite.Url)}/accept", null))
            .EnsureSuccessStatusCode();

        Assert.Empty((await owner.GetFromJsonAsync<List<Summary>>($"/api/v1/playlists/{playlist}/invites"))!);
    }

    [Fact]
    public async Task Only_the_owner_can_invite_people_to_their_playlist()
    {
        var (owner, _) = await NewUserAsync();
        var (stranger, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Not yours {Guid.NewGuid():N}");

        var res = await stranger.PostAsJsonAsync(
            $"/api/v1/playlists/{playlist}/invites", new { role = "Editor" });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    /// <summary>
    /// Following your own link from a second device is ordinary, and not an error — there is
    /// simply no membership to add to a playlist you already own.
    /// </summary>
    [Fact]
    public async Task An_owner_opening_their_own_link_is_not_an_error()
    {
        var (owner, _) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"My own {Guid.NewGuid():N}");

        var invite = await InviteAsync(owner, playlist);

        var res = await owner.PostAsync($"/api/v1/invites/{TokenFrom(invite.Url)}/accept", null);

        res.EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<List<MemberDto>>($"/api/v1/playlists/{playlist}/members"))!);
    }

    /// <summary>
    /// An invitation to do more than they already can is a promotion. One to do less is not a
    /// demotion — somebody's existing access is not taken away by a link.
    /// </summary>
    [Fact]
    public async Task A_smaller_role_does_not_take_away_access_somebody_already_has()
    {
        var (owner, _) = await NewUserAsync();
        var (guest, guestName) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(owner, $"Already in {Guid.NewGuid():N}");

        (await owner.PutAsJsonAsync(
            $"/api/v1/playlists/{playlist}/members/{guestName}", new { role = "Editor" }))
            .EnsureSuccessStatusCode();

        var invite = await InviteAsync(owner, playlist, role: "Viewer");
        (await guest.PostAsync($"/api/v1/invites/{TokenFrom(invite.Url)}/accept", null))
            .EnsureSuccessStatusCode();

        var members = await owner.GetFromJsonAsync<List<MemberDto>>($"/api/v1/playlists/{playlist}/members");
        Assert.Equal("Editor", Assert.Single(members!).Role);
    }
}
