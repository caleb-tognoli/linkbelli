using Linkbelli.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
using Linkbelli.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Linkbelli.Infrastructure.Email;

/// <summary>
/// Sends mail over SMTP, through whatever provider the configuration points at.
/// </summary>
/// <remarks>
/// The only place in the codebase that knows how mail leaves. Brevo, Resend, SES, Postmark and a
/// company relay are all the same five settings to this class, which is the point: the provider
/// is an operational decision, reversible without touching code.
/// </remarks>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    AppMetrics metrics,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    /// <summary>
    /// How long to wait on a mail server.
    /// </summary>
    /// <remarks>
    /// Short. Every caller is either a person waiting on a response or a batch job with more to
    /// get through, and neither is served by holding a connection open for the default two
    /// minutes while a provider has a bad afternoon.
    /// </remarks>
    private const int TimeoutMs = 15_000;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        // Captured once, and non-null past this point: IsConfigured is exactly the check that the
        // host is set, which the compiler cannot see through a property.
        if (_options.Host is not { Length: > 0 } host || !IsConfigured)
        {
            // Not a warning: an instance with no mail configured is a valid way to run this, and
            // logging an error per attempt would bury the ones that matter.
            logger.LogDebug("No mail host configured; not sending \"{Subject}\".", message.Subject);
            return false;
        }

        try
        {
            // Inside the try: an address the mail library cannot parse is a message that cannot be
            // sent, which this method answers with false like any other — not an exception
            // escaping into a caller that was promised a yes or a no.
            var mime = Build(message);

            using var client = new SmtpClient { Timeout = TimeoutMs };

            await client.ConnectAsync(host, _options.Port, SecurityFor(_options.UseTls, _options.Port), cancellationToken);

            // A local catch-all mailbox accepts anything and has no accounts to log in to.
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                // An empty password with a username set is a misconfiguration, but the server is
                // the right thing to hear that from — it answers with which half is wrong.
                await client.AuthenticateAsync(
                    _options.Username, _options.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Sent \"{Subject}\".", message.Subject);
            metrics.Mail(message.Kind, sent: true);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never the recipient's address at Information or above: it is somebody's email, and
            // logs travel further than the data in them.
            logger.LogError(ex, "Could not send \"{Subject}\".", message.Subject);
            metrics.Mail(message.Kind, sent: false);
            return false;
        }
    }

    /// <summary>
    /// How to secure the connection for a given setting and port.
    /// </summary>
    /// <remarks>
    /// 465 is TLS from the first byte; 587 upgrades. Getting this the wrong way round is the single
    /// most common way an SMTP config fails to connect at all, which is why it is its own method
    /// with its own tests.
    /// </remarks>
    internal static SecureSocketOptions SecurityFor(bool useTls, int port) =>
        !useTls
            ? SecureSocketOptions.None
            : port == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

    internal MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        // Both parts, so a client that refuses HTML still shows something usable rather than an
        // empty message with an attachment.
        mime.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        return mime;
    }
}
