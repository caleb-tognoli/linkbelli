using Linkbelli.Application.Email;
using MailKit.Net.Smtp;
using MailKit.Security;
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
        if (!IsConfigured)
        {
            // Not a warning: an instance with no mail configured is a valid way to run this, and
            // logging an error per attempt would bury the ones that matter.
            logger.LogDebug("No mail host configured; not sending \"{Subject}\".", message.Subject);
            return false;
        }

        var mime = Build(message);

        try
        {
            using var client = new SmtpClient { Timeout = TimeoutMs };

            // 465 is TLS from the first byte; 587 upgrades. Getting this the wrong way round is
            // the single most common way an SMTP config fails to connect at all.
            var security = !_options.UseTls
                ? SecureSocketOptions.None
                : _options.Port == 465
                    ? SecureSocketOptions.SslOnConnect
                    : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            // A local catch-all mailbox accepts anything and has no accounts to log in to.
            if (!string.IsNullOrWhiteSpace(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            logger.LogInformation("Sent \"{Subject}\".", message.Subject);
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
            return false;
        }
    }

    private MimeMessage Build(EmailMessage message)
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
