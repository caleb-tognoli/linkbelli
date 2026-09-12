using System.Text;
using Microsoft.AspNetCore.Identity;
using System.Buffers.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Services;

namespace Linkbelli.Application.Email;

/// <summary>
/// Getting back into an account whose password has been forgotten.
/// </summary>
/// <remarks>
/// Before this, a forgotten password meant the account was gone — there was no path back at all.
/// </remarks>
public interface IPasswordResetService
{
    /// <summary>Whether mail is configured, so an endpoint can refuse rather than promise.</summary>
    bool CanSend { get; }

    /// <summary>
    /// Starts a reset for whoever this names, if anybody.
    /// </summary>
    /// <remarks>
    /// Returns nothing on purpose. Whether an account exists is not something an anonymous caller
    /// gets to learn, so there is no outcome here worth reporting differently.
    /// </remarks>
    Task RequestAsync(string login, CancellationToken ct = default);

    /// <summary>Finishes a reset. False when the token is wrong, used, or expired.</summary>
    Task<(bool Ok, string[] Errors)> ResetAsync(
        string email, string token, string newPassword, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PasswordResetService(
    UserManager<ApplicationUser> users,
    IEmailSender email,
    IOptions<EmailOptions> options,
    IAuditLog audit,
    ILogger<PasswordResetService> logger) : IPasswordResetService
{
    /// <summary>
    /// How long a reset link lasts.
    /// </summary>
    /// <remarks>
    /// Identity's own default for these tokens is a day, which is a long time for a link that
    /// grants an account. This is what the mail tells people; the token's real lifetime is
    /// Identity's, so the two are kept in step by configuring it (see AddPasswordResetTokenLifespan).
    /// </remarks>
    public const int ValidForHours = 2;

    private readonly EmailOptions _options = options.Value;

    public bool CanSend => email.IsConfigured;

    public async Task RequestAsync(string login, CancellationToken ct = default)
    {
        var trimmed = login.Trim();

        // Either identifier, because somebody who has forgotten their password may well not
        // remember which of the two they signed up with.
        var user = await users.FindByEmailAsync(trimmed) ?? await users.FindByNameAsync(trimmed);

        if (user?.Email is null)
        {
            // Logged, because an operator watching for a stuck reset needs to see the attempt —
            // and not told to the caller, because that is account enumeration.
            logger.LogInformation("Password reset asked for an account that does not exist.");
            return;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);

        // Base64url, because an Identity token contains characters that a query string mangles,
        // and a token that arrives one byte different is simply an expired link to the person.
        var encoded = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));
        var url =
            $"{_options.PublicUrl.TrimEnd('/')}/reset-password" +
            $"?email={Uri.EscapeDataString(user.Email)}&token={encoded}";

        var sent = await email.SendAsync(
            EmailTemplates.PasswordReset(user.Email, url, ValidForHours), ct);

        if (!sent)
        {
            // Worth an error: somebody is now waiting for a message that is not coming, and the
            // only person who can fix it reads these logs.
            logger.LogError("Could not send a password reset to {UserId}.", user.Id);
        }

        await audit.RecordAsync(
            user.Id, "password.reset.requested", "User", user.Id,
            sent ? "Reset mail sent." : "Reset mail could not be sent.", ct: ct);
    }

    public async Task<(bool Ok, string[] Errors)> ResetAsync(
        string email_, string token, string newPassword, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(email_.Trim());
        if (user is null)
        {
            // The same answer as a bad token, so this cannot be used to test which addresses
            // have accounts.
            return (false, ["That reset link is not valid. Ask for a new one."]);
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            // A truncated link — mail clients wrap long URLs — looks exactly like this.
            return (false, ["That reset link looks incomplete. Ask for a new one."]);
        }

        var result = await users.ResetPasswordAsync(user, decoded, newPassword);
        if (!result.Succeeded)
        {
            // Identity's password-policy messages are worth passing on verbatim: "must contain a
            // digit" is the whole answer. Its token messages are not, and are replaced.
            var errors = result.Errors
                .Select(e => e.Code == "InvalidToken"
                    ? "That reset link has already been used, or has expired. Ask for a new one."
                    : e.Description)
                .Distinct()
                .ToArray();

            return (false, errors);
        }

        // Every existing session dies with the security stamp Identity rotates here, which is the
        // point: a reset is what somebody does when they think another person has the old one.
        await audit.RecordAsync(user.Id, "password.reset.completed", "User", user.Id, ct: ct);

        if (user.Email is not null)
        {
            // Sent to the person who did not do this. A silent password change is how an account
            // is lost without anybody noticing.
            await email.SendAsync(EmailTemplates.PasswordChanged(user.Email, _options.PublicUrl), ct);
        }

        return (true, []);
    }
}
