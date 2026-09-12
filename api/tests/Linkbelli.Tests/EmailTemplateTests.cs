using Linkbelli.Application.Email;

namespace Linkbelli.Tests;

/// <summary>
/// What somebody actually receives. Every one of these messages interpolates something a person
/// chose — a playlist name, a link title — so the escaping is the difference between a digest and
/// an injection, and the text part is the difference between a usable reset and a blank message.
/// </summary>
public class EmailTemplateTests
{
    [Fact]
    public void A_reset_carries_the_link_in_both_parts()
    {
        const string url = "https://linkbelli.example/reset-password?email=a%40b.com&token=abc";

        var message = EmailTemplates.PasswordReset("a@b.com", url, 2);

        // A client that refuses HTML must still leave something a person can act on.
        Assert.Contains(url, message.TextBody);
        Assert.Contains("abc", message.HtmlBody);
        Assert.Equal("a@b.com", message.To);
    }

    [Fact]
    public void A_reset_says_how_long_it_lasts_and_gets_the_plural_right()
    {
        Assert.Contains("2 hours", EmailTemplates.PasswordReset("a@b.com", "https://x", 2).TextBody);
        Assert.Contains("1 hour", EmailTemplates.PasswordReset("a@b.com", "https://x", 1).TextBody);
    }

    [Fact]
    public void A_reset_tells_somebody_who_did_not_ask_that_nothing_has_happened()
    {
        var message = EmailTemplates.PasswordReset("a@b.com", "https://x", 2);

        // The most likely recipient of an unexpected one of these is the person being attacked.
        Assert.Contains("wasn't you", message.TextBody);
        Assert.Contains("has not changed", message.TextBody);
    }

    [Fact]
    public void A_changed_password_offers_a_way_back_to_whoever_did_not_do_it()
    {
        var message = EmailTemplates.PasswordChanged("a@b.com", "https://linkbelli.example");

        Assert.Contains("https://linkbelli.example/forgot-password", message.TextBody);
        Assert.Contains("can currently sign in", message.TextBody);
    }

    [Fact]
    public void A_trailing_slash_on_the_public_url_does_not_double_up()
    {
        var message = EmailTemplates.PasswordChanged("a@b.com", "https://linkbelli.example/");

        Assert.DoesNotContain("example//", message.TextBody);
    }

    [Fact]
    public void Html_from_a_playlist_name_is_escaped_not_rendered()
    {
        var message = EmailTemplates.Digest(
            "a@b.com",
            "https://x",
            addedThisWeek: 1,
            unread: 0,
            highlights: [new EmailTemplates.NotificationLine("<script>alert(1)</script>", "https://x/1")],
            quiet: []);

        // A link title comes off the open web, and a playlist name is whatever somebody typed.
        Assert.DoesNotContain("<script>", message.HtmlBody);
        Assert.Contains("&lt;script&gt;", message.HtmlBody);
    }

    [Fact]
    public void An_ampersand_in_a_title_does_not_break_the_html()
    {
        var message = EmailTemplates.Notification(
            "a@b.com", "s", "h", "intro",
            [new EmailTemplates.NotificationLine("Marks & Spencer")],
            "https://x");

        Assert.Contains("Marks &amp; Spencer", message.HtmlBody);
    }

    [Fact]
    public void A_quiet_week_says_so_rather_than_reporting_zero()
    {
        var message = EmailTemplates.Digest("a@b.com", "https://x", 0, 0, [], []);

        Assert.Contains("Nothing new arrived", message.TextBody);
    }

    [Fact]
    public void A_digest_gets_singulars_right()
    {
        var one = EmailTemplates.Digest("a@b.com", "https://x", 1, 1, [], []);

        Assert.Contains("1 link arrived", one.TextBody);
        Assert.Contains("1 link still unread", one.TextBody);

        var many = EmailTemplates.Digest("a@b.com", "https://x", 4, 9, [], []);

        Assert.Contains("4 links arrived", many.TextBody);
        Assert.Contains("9 links still unread", many.TextBody);
    }

    [Fact]
    public void A_digest_names_the_sources_that_found_nothing()
    {
        var one = EmailTemplates.Digest("a@b.com", "https://x", 3, 0, [], ["BBC News"]);
        var two = EmailTemplates.Digest("a@b.com", "https://x", 3, 0, [], ["BBC News", "Hacker News"]);

        // Counted, this would be a statistic. Named, it is something to go and look at.
        Assert.Contains("One source found nothing all week: BBC News", one.TextBody);
        Assert.Contains("BBC News, Hacker News", two.TextBody);
    }

    [Fact]
    public void A_digest_leaves_out_what_there_is_nothing_to_say_about()
    {
        var message = EmailTemplates.Digest("a@b.com", "https://x", 5, 0, [], []);

        Assert.DoesNotContain("unread", message.TextBody);
        Assert.DoesNotContain("found nothing", message.TextBody);
    }

    [Fact]
    public void Every_message_has_both_bodies()
    {
        EmailMessage[] all =
        [
            EmailTemplates.PasswordReset("a@b.com", "https://x", 2),
            EmailTemplates.PasswordChanged("a@b.com", "https://x"),
            EmailTemplates.Notification("a@b.com", "s", "h", "i", [], "https://x"),
            EmailTemplates.Digest("a@b.com", "https://x", 1, 1, [], []),
        ];

        Assert.All(all, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.TextBody));
            Assert.False(string.IsNullOrWhiteSpace(m.HtmlBody));
            Assert.False(string.IsNullOrWhiteSpace(m.Subject));
        });
    }
}
