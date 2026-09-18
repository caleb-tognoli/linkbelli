using Linkbelli.Application.Email;
using Linkbelli.Application.Observability;
using Linkbelli.Infrastructure.Email;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Linkbelli.Infrastructure.Tests;

/// <summary>The parts of sending mail that can be checked without a mail server.</summary>
public class SmtpEmailSenderTests
{
    private static SmtpEmailSender Sender(Action<EmailOptions>? configure = null)
    {
        var options = new EmailOptions
        {
            Host = "mail.invalid",
            Port = 587,
            UseTls = true,
            FromAddress = "no-reply@linkbelli.test",
            FromName = "Linkbelli",
        };
        configure?.Invoke(options);

        return new SmtpEmailSender(Options.Create(options), new AppMetrics(), NullLogger<SmtpEmailSender>.Instance);
    }

    private static EmailMessage Message(string to = "reader@example.com") =>
        new(to, "Your week", "Plain words.", "<p>Formatted words.</p>", "digest");

    /// <summary>
    /// The single most common way an SMTP setup fails to connect at all: 465 speaks TLS from the
    /// first byte, 587 starts plain and upgrades. The wrong one hangs or is refused outright.
    /// </summary>
    [Theory]
    [InlineData(true, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(true, 587, SecureSocketOptions.StartTls)]
    [InlineData(true, 25, SecureSocketOptions.StartTls)]
    [InlineData(false, 465, SecureSocketOptions.None)]
    [InlineData(false, 1025, SecureSocketOptions.None)] // a local catch-all like Mailpit
    public void The_connection_is_secured_the_way_the_port_expects(bool useTls, int port, SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpEmailSender.SecurityFor(useTls, port));
    }

    [Fact]
    public void A_message_carries_both_a_plain_and_a_formatted_version()
    {
        var mime = Sender().Build(Message());

        // A client that refuses HTML still shows something, rather than an empty message.
        Assert.Equal("Plain words.", mime.TextBody?.Trim());
        Assert.Contains("Formatted words.", mime.HtmlBody);
        Assert.IsType<MultipartAlternative>(mime.Body);
    }

    [Fact]
    public void A_message_is_from_the_configured_sender_and_to_the_person()
    {
        var mime = Sender(o => o.FromName = "Linkbelli at home").Build(Message("reader@example.com"));

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal("no-reply@linkbelli.test", from.Address);
        Assert.Equal("Linkbelli at home", from.Name);
        Assert.Equal("reader@example.com", Assert.IsType<MailboxAddress>(Assert.Single(mime.To)).Address);
        Assert.Equal("Your week", mime.Subject);
    }

    /// <summary>An instance with no mail configured is a valid way to run this, not a failure.</summary>
    [Fact]
    public async Task With_no_host_nothing_is_attempted()
    {
        var sender = Sender(o => o.Host = null);

        Assert.False(sender.IsConfigured);
        Assert.False(await sender.SendAsync(Message()));
    }

    /// <summary>
    /// The method promises a yes or a no. An address the mail library cannot parse used to be
    /// read outside the error handling, so it escaped as an exception into whatever was sending —
    /// a digest sweep, say, which would then stop partway through everybody else's.
    /// </summary>
    [Fact]
    public async Task An_address_that_cannot_be_parsed_is_a_no_rather_than_an_exception()
    {
        Assert.False(await Sender().SendAsync(Message("not an address at all")));
    }
}
