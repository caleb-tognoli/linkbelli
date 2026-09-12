namespace Linkbelli.Application.Email;

/// <summary>One message to send.</summary>
/// <remarks>
/// Both bodies, always. A password reset that arrives as HTML in a client that will not render it
/// is a password reset nobody can use, and a plain-text part costs one string.
/// </remarks>
public record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string HtmlBody);

/// <summary>
/// Sends mail, without saying who through.
/// </summary>
/// <remarks>
/// Deliberately the whole surface. Every provider worth using speaks SMTP, so the choice between
/// Brevo, Resend, SES or a company relay is configuration rather than code — and this interface
/// lives in Application, which has no SMTP dependency of its own, so nothing above
/// Infrastructure can accidentally start depending on one.
/// </remarks>
public interface IEmailSender
{
    /// <summary>
    /// Whether mail can actually go out.
    /// </summary>
    /// <remarks>
    /// A feature that emails somebody has to know this. Offering a password reset on an instance
    /// with no mail configured produces a promise of a message that will never arrive, which is
    /// worse than saying the feature is unavailable.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>
    /// Sends one message. Returns false when it could not be sent.
    /// </summary>
    /// <remarks>
    /// Returns rather than throws: every caller is a background job or an endpoint that must not
    /// fail because a mail server was briefly unreachable, and each of them wants to decide for
    /// itself whether to retry, skip, or carry on.
    /// </remarks>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
