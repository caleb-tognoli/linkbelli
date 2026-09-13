using System.Buffers.Text;
using System.Text;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Linkbelli.Application.Email;

/// <summary>
/// Proving that an address belongs to whoever typed it.
/// </summary>
/// <remarks>
/// Registration took an address on trust and every outbound feature mailed it. Two things went
/// wrong at once. The instance became a small spam cannon: register as somebody else, and they
/// get a weekly digest from your domain, and your sending reputation takes the hit. And because
/// <c>RequireUniqueEmail</c> is on, squatting an address denied its real owner an account.
///
/// What is gated is the <em>outbound mail</em>, not signing in. An instance with no mail
/// configured has no way to confirm anything, and locking those people out of their own accounts
/// to close a mail problem would be a worse trade than the problem. So an unconfirmed account
/// works normally and is simply never written to — which is exactly what the victim of the
/// squatting wanted.
/// </remarks>
public interface IEmailVerificationService
{
    /// <summary>Whether mail is configured, so a caller can refuse rather than promise.</summary>
    bool CanSend { get; }

    /// <summary>
    /// Sends a confirmation link, if there is anywhere to send it and anything to confirm.
    /// </summary>
    /// <remarks>
    /// Returns nothing. Called from registration, where a mail failure must not fail the signup,
    /// and from a resend endpoint, where whether an address has an account is not something an
    /// anonymous caller gets to learn.
    /// </remarks>
    Task SendAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>Sends to whoever this address belongs to, if anybody. Silent either way.</summary>
    Task ResendAsync(string email, CancellationToken ct = default);

    /// <summary>Finishes it. False when the token is wrong, used, or expired.</summary>
    Task<(bool Ok, string Error)> ConfirmAsync(string email, string token, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class EmailVerificationService(
    UserManager<ApplicationUser> users,
    IEmailSender email,
    IOptions<EmailOptions> options,
    IAuditLog audit,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    /// <summary>
    /// How long a confirmation link lasts.
    /// </summary>
    /// <remarks>
    /// Longer than a password reset's two hours, and for the opposite reason: nobody is waiting
    /// at the keyboard for this one. It arrives while you are doing something else, and a link
    /// that expires before you next open your mail is a link that makes people ask for another.
    /// </remarks>
    public const int ValidForHours = 24;

    private readonly EmailOptions _options = options.Value;

    public bool CanSend => email.IsConfigured;

    public async Task SendAsync(ApplicationUser user, CancellationToken ct = default)
    {
        if (!CanSend || user.Email is null || user.EmailConfirmed)
        {
            return;
        }

        var token = await users.GenerateEmailConfirmationTokenAsync(user);

        // Base64url, for the same reason the reset link is: an Identity token contains characters
        // a query string mangles, and a token that arrives one byte different is simply an
        // expired link to the person holding it.
        var encoded = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
        var url =
            $"{_options.PublicUrl.TrimEnd('/')}/confirm-email" +
            $"?email={Uri.EscapeDataString(user.Email)}&token={encoded}";

        var sent = await email.SendAsync(
            EmailTemplates.ConfirmEmail(user.Email, url, ValidForHours), ct);

        if (!sent)
        {
            // Worth an error: somebody is now waiting for a message that is not coming, and the
            // only person who can fix it reads these logs.
            logger.LogError("Could not send an address confirmation to {UserId}.", user.Id);
        }
    }

    public async Task ResendAsync(string email_, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email_.Trim());
        if (user is null)
        {
            // Logged and not told to the caller: which addresses have accounts is exactly what
            // this endpoint must not answer.
            logger.LogInformation("Confirmation resend asked for an address with no account.");
            return;
        }

        await SendAsync(user, ct);
    }

    public async Task<(bool Ok, string Error)> ConfirmAsync(
        string email_, string token, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email_.Trim());
        if (user is null)
        {
            // The same answer as a bad token, so this cannot be used to test which addresses
            // have accounts.
            return (false, "That confirmation link is not valid. Ask for a new one.");
        }

        if (user.EmailConfirmed)
        {
            // Not an error. Clicking the link twice, or from two devices, is ordinary — and
            // telling somebody their confirmation failed when their address is confirmed would
            // send them looking for a problem that does not exist.
            return (true, string.Empty);
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            // A truncated link — mail clients wrap long URLs — looks exactly like this.
            return (false, "That confirmation link looks incomplete. Ask for a new one.");
        }

        var result = await users.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded)
        {
            return (false, "That confirmation link has already been used, or has expired. Ask for a new one.");
        }

        await audit.RecordAsync(user.Id, "email.confirmed", "User", user.Id, ct: ct);
        return (true, string.Empty);
    }
}
