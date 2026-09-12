using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Getting back into an account whose password has been forgotten — which until now meant the
/// account was simply gone. These cover the whole round trip through the link in the mail, and
/// the two things this endpoint must not do: tell a stranger who has an account, and let one
/// link be used twice.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PasswordResetTests(PostgresApiFactory factory)
{
    private record TokenResponse(string AccessToken);

    private async Task<(string Username, string Email, string Password)> NewUserAsync()
    {
        var username = NewUsername();
        var email = $"{username}@example.com";
        const string password = "Passw0rd!x";

        var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/register", new { username, email, password }))
            .EnsureSuccessStatusCode();

        return (username, email, password);
    }

    private async Task<HttpResponseMessage> ForgotAsync(string login) =>
        await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/forgot-password", new { login });

    private async Task<HttpResponseMessage> ResetAsync(string email, string token, string newPassword) =>
        await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/reset-password", new { email, token, newPassword });

    private async Task<bool> CanSignInAsync(string login, string password)
    {
        var res = await factory.CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new { login, password });

        return res.IsSuccessStatusCode;
    }

    /// <summary>Pulls the token out of the link, the way a person's browser would.</summary>
    private static string TokenFromMail(string body)
    {
        var match = Regex.Match(body, @"token=([A-Za-z0-9_\-]+)");
        Assert.True(match.Success, $"No reset token in the message:\n{body}");

        return match.Groups[1].Value;
    }

    [Fact]
    public async Task A_reset_link_arrives_and_works()
    {
        var (_, email, oldPassword) = await NewUserAsync();
        factory.Email.Clear();

        (await ForgotAsync(email)).EnsureSuccessStatusCode();

        var mail = factory.Email.LastTo(email);
        Assert.NotNull(mail);
        var token = TokenFromMail(mail!.TextBody);

        (await ResetAsync(email, token, "N3wPassw0rd!")).EnsureSuccessStatusCode();

        Assert.True(await CanSignInAsync(email, "N3wPassw0rd!"));
        Assert.False(await CanSignInAsync(email, oldPassword));
    }

    [Fact]
    public async Task The_username_works_as_well_as_the_email()
    {
        var (username, email, _) = await NewUserAsync();
        factory.Email.Clear();

        // Somebody who has forgotten their password may not remember which they signed up with.
        (await ForgotAsync(username)).EnsureSuccessStatusCode();

        Assert.NotNull(factory.Email.LastTo(email));
    }

    [Fact]
    public async Task An_account_that_does_not_exist_gets_the_same_answer()
    {
        factory.Email.Clear();

        var res = await ForgotAsync($"nobody-{Guid.NewGuid():N}@example.com");

        // Any difference here — a status, a message, a delay — turns this into a way to find out
        // who has an account.
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Empty(factory.Email.Sent);
    }

    [Fact]
    public async Task A_link_only_works_once()
    {
        var (_, email, _) = await NewUserAsync();
        factory.Email.Clear();
        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var token = TokenFromMail(factory.Email.LastTo(email)!.TextBody);

        (await ResetAsync(email, token, "First0ne!x")).EnsureSuccessStatusCode();
        var second = await ResetAsync(email, token, "Second0ne!x");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("already been used", await second.Content.ReadAsStringAsync());
        // And the first password still stands, rather than the second attempt half-applying.
        Assert.True(await CanSignInAsync(email, "First0ne!x"));
    }

    [Fact]
    public async Task A_made_up_token_is_refused()
    {
        var (_, email, oldPassword) = await NewUserAsync();

        var res = await ResetAsync(email, "bm90LWEtcmVhbC10b2tlbg", "N3wPassw0rd!");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(await CanSignInAsync(email, oldPassword));
    }

    [Fact]
    public async Task A_token_that_is_not_even_base64_is_refused_kindly()
    {
        var (_, email, _) = await NewUserAsync();

        var res = await ResetAsync(email, "!!!not-base64!!!", "N3wPassw0rd!");

        // What a mail client wrapping a long URL actually produces.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("incomplete", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Somebody_elses_token_cannot_reset_your_password()
    {
        var (_, victimEmail, victimPassword) = await NewUserAsync();
        var (_, attackerEmail, _) = await NewUserAsync();
        factory.Email.Clear();

        (await ForgotAsync(attackerEmail)).EnsureSuccessStatusCode();
        var attackerToken = TokenFromMail(factory.Email.LastTo(attackerEmail)!.TextBody);

        var res = await ResetAsync(victimEmail, attackerToken, "Pwn3dPassw0rd!");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.True(await CanSignInAsync(victimEmail, victimPassword));
    }

    [Fact]
    public async Task An_address_with_no_account_is_refused_the_same_way_as_a_bad_token()
    {
        var res = await ResetAsync($"nobody-{Guid.NewGuid():N}@example.com", "c29tZS10b2tlbg", "N3wPassw0rd!");

        // Otherwise this endpoint answers "does this address have an account" too.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not valid", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_password_that_fails_the_policy_says_what_is_wrong()
    {
        var (_, email, _) = await NewUserAsync();
        factory.Email.Clear();
        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var token = TokenFromMail(factory.Email.LastTo(email)!.TextBody);

        var res = await ResetAsync(email, token, "a");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        // Identity's own wording, which is the useful part — "too short" beats "invalid".
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Changing_a_password_tells_the_account_it_happened()
    {
        var (_, email, _) = await NewUserAsync();
        factory.Email.Clear();
        (await ForgotAsync(email)).EnsureSuccessStatusCode();
        var token = TokenFromMail(factory.Email.LastTo(email)!.TextBody);

        (await ResetAsync(email, token, "N3wPassw0rd!")).EnsureSuccessStatusCode();

        // The recipient who matters is the one who did not do this: a silent change is how an
        // account is lost without anybody noticing.
        var confirmation = factory.Email.LastTo(email);
        Assert.Contains("changed", confirmation!.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/forgot-password", confirmation.TextBody);
    }

    [Fact]
    public async Task The_link_points_at_the_web_app_not_the_api()
    {
        var (_, email, _) = await NewUserAsync();
        factory.Email.Clear();

        (await ForgotAsync(email)).EnsureSuccessStatusCode();

        var mail = factory.Email.LastTo(email)!;
        // Built from configuration, never from an inbound Host header — otherwise a request can
        // make us mail somebody a link to somewhere else.
        Assert.Contains("https://linkbelli.test/reset-password", mail.TextBody);
        Assert.Contains($"email={HttpUtility.UrlEncode(email)}", mail.TextBody);
    }

    [Fact]
    public async Task A_blank_login_is_a_validation_error_not_a_silent_success()
    {
        var res = await ForgotAsync("   ");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Mail_that_cannot_be_sent_still_does_not_reveal_the_account()
    {
        var (_, email, _) = await NewUserAsync();
        factory.Email.Clear();
        factory.Email.Fail = true;

        try
        {
            var res = await ForgotAsync(email);

            // The caller is told the same thing either way. Whether the message got out is the
            // operator's problem, and it is in the logs.
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        }
        finally
        {
            factory.Email.Fail = false;
        }
    }
}
