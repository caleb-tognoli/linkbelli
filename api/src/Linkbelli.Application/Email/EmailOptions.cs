namespace Linkbelli.Application.Email;

/// <summary>
/// Where to send mail through, and who it comes from.
/// </summary>
/// <remarks>
/// Plain SMTP, because that is the one thing every provider offers. Switching from Brevo to
/// Resend, SES or an internal relay is these five values and nothing else — no new package, no
/// code change, no redeploy of anything but configuration.
///
/// Bound from the <c>Email</c> section, so in Docker these are <c>Email__Host</c> and friends.
/// </remarks>
public class EmailOptions
{
    public const string Section = "Email";

    /// <summary>
    /// The SMTP host. Empty means mail is switched off, which is the default: an instance that
    /// has not been told how to send mail must not pretend it can.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// 587 for STARTTLS, which is what Brevo, Resend and SES all want. 465 for implicit TLS.
    /// </summary>
    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    /// <summary>
    /// The provider's SMTP password or API key. A secret: set it from the environment or a
    /// secret store, never in a committed appsettings file.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>The address mail comes from. Must be one the provider has verified for the domain.</summary>
    public string FromAddress { get; set; } = "no-reply@localhost";

    public string FromName { get; set; } = "Linkbelli";

    /// <summary>
    /// Whether to require TLS.
    /// </summary>
    /// <remarks>
    /// On everywhere real. Off only for a local catch-all mailbox like Mailpit, which speaks
    /// plaintext on purpose and never leaves the machine — every other case is credentials
    /// crossing a network.
    /// </remarks>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Where links in mail should point — the address a person will click.
    /// </summary>
    /// <remarks>
    /// Not derived from the request. A password reset link built from an inbound Host header is
    /// a way to send somebody a link to an attacker's site, and the mail is often sent from a
    /// background job that has no request to look at anyway.
    /// </remarks>
    public string PublicUrl { get; set; } = "http://localhost:5173";

    /// <summary>Whether there is enough here to try sending at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
