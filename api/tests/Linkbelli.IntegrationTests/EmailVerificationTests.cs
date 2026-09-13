using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Proving an address belongs to whoever typed it.
/// </summary>
/// <remarks>
/// Registration took the address on trust and every outbound feature mailed it. Two things went
/// wrong at once: the instance became a small spam cannon aimed at a stranger — sign up as
/// somebody else and they get a weekly digest from your domain — and because addresses are
/// unique, squatting one denied its real owner an account.
///
/// What is gated is the outbound mail, not signing in. An instance with no mail configured cannot
/// confirm anything, and locking those people out of their own accounts to close a mail problem
/// would be the worse trade.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class EmailVerificationTests(PostgresApiFactory factory)
{
    private async Task<(string Username, string Email)> RegisterAsync()
    {
        var username = NewUsername();
        var email = $"{username}@example.com";

        factory.Email.Clear();
        (await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/register", new { username, email, password = "Passw0rd!x" }))
            .EnsureSuccessStatusCode();

        return (username, email);
    }

    /// <summary>Pulls the token out of the link, the way a person's browser would.</summary>
    private static string TokenFromMail(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-]+)");
        Assert.True(match.Success, $"No confirmation token in the message:\n{body}");

        return match.Groups[1].Value;
    }

    private async Task<HttpResponseMessage> ConfirmAsync(string email, string token) =>
        await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/confirm-email", new { email, token });

    [Fact]
    public async Task Signing_up_sends_a_confirmation_link_that_works()
    {
        var (_, email) = await RegisterAsync();

        var mail = factory.Email.LastTo(email);
        Assert.NotNull(mail);
        Assert.Contains("confirm", mail!.Subject, StringComparison.OrdinalIgnoreCase);

        var confirmed = await ConfirmAsync(email, TokenFromMail(mail.TextBody));

        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    /// <summary>
    /// Confirming from two devices, or clicking the link twice, is ordinary. Reporting a failure
    /// would send somebody looking for a problem that does not exist.
    /// </summary>
    [Fact]
    public async Task Clicking_the_link_twice_is_not_an_error()
    {
        var (_, email) = await RegisterAsync();
        var token = TokenFromMail(factory.Email.LastTo(email)!.TextBody);

        (await ConfirmAsync(email, token)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await ConfirmAsync(email, token)).StatusCode);
    }

    [Fact]
    public async Task A_token_from_nowhere_is_refused()
    {
        var (_, email) = await RegisterAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await ConfirmAsync(email, "not-a-token")).StatusCode);
    }

    /// <summary>
    /// The same answer for an address with no account as for a bad token. Anything else turns
    /// this into a way to find out who has one.
    /// </summary>
    [Fact]
    public async Task An_address_with_no_account_answers_the_same_as_a_bad_token()
    {
        var (_, email) = await RegisterAsync();
        var token = TokenFromMail(factory.Email.LastTo(email)!.TextBody);

        var stranger = await ConfirmAsync($"{NewUsername()}@example.com", token);
        var badToken = await ConfirmAsync(email, "not-a-token");

        Assert.Equal(badToken.StatusCode, stranger.StatusCode);
    }

    /// <summary>The point of the whole thing.</summary>
    [Fact]
    public async Task Nothing_is_mailed_to_an_unconfirmed_address()
    {
        var (username, email) = await RegisterAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await SignInAsync(username));

        factory.Email.Clear();

        // "Send me one now", pressed by somebody who may not own the address it would go to.
        var preview = await client.PostAsync("/api/v1/notifications/digest/preview", null);

        Assert.Null(factory.Email.LastTo(email));
        Assert.False(preview.IsSuccessStatusCode, "A digest went to an unconfirmed address.");
    }

    [Fact]
    public async Task Once_confirmed_the_mail_arrives_again()
    {
        var (username, email) = await RegisterAsync();
        await ConfirmAsync(email, TokenFromMail(factory.Email.LastTo(email)!.TextBody));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await SignInAsync(username));

        factory.Email.Clear();
        (await client.PostAsync("/api/v1/notifications/digest/preview", null)).EnsureSuccessStatusCode();

        Assert.NotNull(factory.Email.LastTo(email));
    }

    /// <summary>
    /// Not being able to receive mail must not mean not being able to use the account. An
    /// instance with no mail configured cannot confirm anything at all.
    /// </summary>
    [Fact]
    public async Task An_unconfirmed_account_still_signs_in_and_works()
    {
        var (username, _) = await RegisterAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await SignInAsync(username));

        var created = await client.PostAsJsonAsync("/api/v1/playlists", new { name = "Still mine" });

        created.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Another_link_can_be_asked_for()
    {
        var (_, email) = await RegisterAsync();
        factory.Email.Clear();

        var asked = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email });

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.NotNull(factory.Email.LastTo(email));
    }

    /// <summary>
    /// The same answer whether or not that address has an account — otherwise this endpoint is
    /// an address checker.
    /// </summary>
    [Fact]
    public async Task Asking_for_an_address_with_no_account_says_the_same_thing()
    {
        var stranger = $"{NewUsername()}@example.com";

        var asked = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email = stranger });

        Assert.Equal(HttpStatusCode.Accepted, asked.StatusCode);
        Assert.Null(factory.Email.LastTo(stranger));
    }

    /// <summary>An address already confirmed does not get told about it again.</summary>
    [Fact]
    public async Task Asking_again_after_confirming_sends_nothing()
    {
        var (_, email) = await RegisterAsync();
        await ConfirmAsync(email, TokenFromMail(factory.Email.LastTo(email)!.TextBody));

        factory.Email.Clear();
        (await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email }))
            .EnsureSuccessStatusCode();

        Assert.Null(factory.Email.LastTo(email));
    }

    private async Task<string> SignInAsync(string username)
    {
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = "Passw0rd!x" });
        res.EnsureSuccessStatusCode();

        return (await res.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;
    }
}
