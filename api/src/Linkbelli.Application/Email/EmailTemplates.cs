using System.Net;
using System.Text;

namespace Linkbelli.Application.Email;

/// <summary>
/// The messages Linkbelli sends, as text.
/// </summary>
/// <remarks>
/// Composed in code rather than from template files: there are four of them, they are all short,
/// and a templating engine would add a dependency and a build step to solve a problem this size.
/// Pure functions, so what somebody actually receives is unit-testable without a mail server.
/// </remarks>
public static class EmailTemplates
{
    /// <summary>
    /// A plain-text and HTML pair, wrapped the same way every time.
    /// </summary>
    /// <remarks>
    /// Inline styles and a table-free layout, because mail clients are not browsers: no external
    /// stylesheet is fetched, and anything cleverer than this renders differently in Outlook.
    /// </remarks>
    private static EmailMessage Compose(string kind, string to, string subject, string heading, string[] paragraphs, (string Label, string Url)? button = null)
    {
        var text = new StringBuilder();
        text.AppendLine(heading);
        text.AppendLine();
        foreach (var paragraph in paragraphs)
        {
            text.AppendLine(paragraph);
            text.AppendLine();
        }

        if (button is { } b)
        {
            // The URL on its own line in the text part: a client that does not linkify it still
            // leaves something a person can copy.
            text.AppendLine(b.Url);
            text.AppendLine();
        }

        text.AppendLine("— Linkbelli");

        var html = new StringBuilder();
        html.Append("""<div style="font-family:system-ui,-apple-system,Segoe UI,sans-serif;font-size:15px;line-height:1.55;color:#111;max-width:34rem">""");
        html.Append($"<h1 style=\"font-size:1.15rem;margin:0 0 1rem\">{Escape(heading)}</h1>");

        foreach (var paragraph in paragraphs)
        {
            html.Append($"<p style=\"margin:0 0 1rem\">{Escape(paragraph)}</p>");
        }

        if (button is { } btn)
        {
            html.Append(
                $"""<p style="margin:0 0 1rem"><a href="{Escape(btn.Url)}" style="display:inline-block;background:#2563eb;color:#fff;text-decoration:none;padding:.6rem 1rem;border-radius:.375rem">{Escape(btn.Label)}</a></p>""");
            html.Append(
                $"""<p style="margin:0 0 1rem;font-size:13px;color:#666">Or paste this in: <br><span style="word-break:break-all">{Escape(btn.Url)}</span></p>""");
        }

        html.Append("""<p style="margin:1.5rem 0 0;font-size:13px;color:#666">— Linkbelli</p>""");
        html.Append("</div>");

        return new EmailMessage(to, subject, text.ToString(), html.ToString(), kind);
    }

    /// <summary>
    /// Escapes anything going into the HTML part.
    /// </summary>
    /// <remarks>
    /// Every one of these messages interpolates something a person chose — a playlist name, a
    /// link title, a username — so this is the difference between a digest and an injection.
    /// </remarks>
    private static string Escape(string value) => WebUtility.HtmlEncode(value);

    /// <summary>
    /// Sent once, to prove the address belongs to whoever typed it.
    /// </summary>
    /// <remarks>
    /// Anybody could register with anybody's address, and every outbound feature — the digest,
    /// notifications — then mailed it. Two things went wrong at once: the instance became a small
    /// spam cannon aimed at a stranger, and because addresses are unique, the squatter denied the
    /// real owner an account.
    /// </remarks>
    public static EmailMessage ConfirmEmail(string to, string confirmUrl, int validForHours) =>
        Compose(
            "confirm-email",
            to,
            "Confirm your Linkbelli address",
            "Confirm your address",
            [
                "Somebody signed up to Linkbelli with this address. If that was you, confirm it with the link below.",
                $"It works once, and stops working after {validForHours} {(validForHours == 1 ? "hour" : "hours")}.",
                "If it wasn't you, ignore this. Nothing will be sent to you again, and the address stays free for you to use.",
            ],
            ("Confirm this address", confirmUrl));

    /// <summary>
    /// Somebody asking you to join a playlist.
    /// </summary>
    /// <remarks>
    /// The name of whoever sent it and the name of the list are both things a person chose, so
    /// both go through the escaper — which is the difference between an invitation and an
    /// injection.
    /// </remarks>
    public static EmailMessage PlaylistInvite(
        string to, string from, string playlistName, string url, int validForDays) =>
        Compose(
            "playlist-invite",
            to,
            $"{from} shared a playlist with you on Linkbelli",
            "You have been invited",
            [
                $"{from} would like you to join \"{playlistName}\" on Linkbelli.",
                $"The link works once, and stops working after {validForDays} days.",
                "You do not need an account yet — you can make one on the way in.",
            ],
            ("Open the playlist", url));

    public static EmailMessage PasswordReset(string to, string resetUrl, int validForHours) =>
        Compose(
            "password-reset",
            to,
            "Reset your Linkbelli password",
            "Reset your password",
            [
                "Somebody asked to reset the password on your Linkbelli account. If that was you, use the link below.",
                $"It works once, and stops working after {validForHours} {(validForHours == 1 ? "hour" : "hours")}.",
                "If it wasn't you, nothing has happened yet and you can ignore this. Your password has not changed.",
            ],
            ("Choose a new password", resetUrl));

    /// <summary>
    /// Sent after a password actually changes.
    /// </summary>
    /// <remarks>
    /// The point of this one is the person who did not do it: a silent password change is how
    /// somebody loses an account without noticing.
    /// </remarks>
    public static EmailMessage PasswordChanged(string to, string publicUrl) =>
        Compose(
            "password-changed",
            to,
            "Your Linkbelli password was changed",
            "Your password was changed",
            [
                "The password on your Linkbelli account has just been changed.",
                "If that was you, there is nothing to do.",
                "If it wasn't, reset it now — whoever changed it can currently sign in.",
            ],
            ("Reset it", $"{publicUrl.TrimEnd('/')}/forgot-password"));

    /// <summary>One thing worth telling somebody about, as a line in a notification mail.</summary>
    public record NotificationLine(string Text, string? Url = null);

    /// <summary>A passage somebody marked, and the article it came from.</summary>
    public record QuotedLine(string Quote, string Source, string Url);

    public static EmailMessage Notification(
        string to, string subject, string heading, string intro, IReadOnlyList<NotificationLine> lines, string publicUrl)
    {
        var body = new List<string> { intro };
        foreach (var line in lines)
        {
            body.Add(line.Url is null ? $"• {line.Text}" : $"• {line.Text} — {line.Url}");
        }

        return Compose("notification", to, subject, heading, [.. body], ("Open Linkbelli", publicUrl.TrimEnd('/')));
    }

    /// <summary>
    /// Adds the line that lets somebody stop receiving this kind of message.
    /// </summary>
    /// <remarks>
    /// Appended rather than built in, because the two messages that must never carry one are the
    /// password reset and the password-changed notice. Those are not marketing, and an
    /// unsubscribe link on them would be a way to stop somebody hearing that their account was
    /// taken.
    /// </remarks>
    public static EmailMessage WithUnsubscribe(EmailMessage message, string unsubscribeUrl, NotificationKind kind)
    {
        var sentence = $"You are getting this because Linkbelli tells you {kind.Describe()}.";

        var text = $"{message.TextBody}\n\n{sentence}\nStop these: {unsubscribeUrl}\n";

        var footer = $"<p style=\"margin:1rem 0 0;font-size:12px;color:#888\">{Escape(sentence)} <a href=\"{Escape(unsubscribeUrl)}\" style=\"color:#888\">Stop these</a>.</p>";

        return message with { TextBody = text, HtmlBody = message.HtmlBody + footer };
    }

    /// <summary>What a week produced, for somebody who asked to hear about it.</summary>
    public static EmailMessage Digest(
        string to,
        string publicUrl,
        int addedThisWeek,
        int unread,
        IReadOnlyList<NotificationLine> arrivals,
        IReadOnlyList<string> quiet,
        IReadOnlyList<QuotedLine>? marked = null)
    {
        var paragraphs = new List<string>
        {
            addedThisWeek == 0
                ? "Nothing new arrived this week."
                : $"{addedThisWeek} {(addedThisWeek == 1 ? "link" : "links")} arrived this week.",
        };

        if (arrivals.Count > 0)
        {
            paragraphs.Add("Some of what came in:");
            paragraphs.AddRange(arrivals.Select(h => h.Url is null ? $"• {h.Text}" : $"• {h.Text} — {h.Url}"));
        }

        // Quoted back in their own words: the most personal thing a summary of somebody's week
        // can say is what they stopped at while reading it.
        if (marked is { Count: > 0 })
        {
            paragraphs.Add(marked.Count == 1 ? "Something you marked:" : "Some of what you marked:");
            paragraphs.AddRange(marked.Select(m => $"“{m.Quote}” — {m.Source}, {m.Url}"));
        }

        if (unread > 0)
        {
            paragraphs.Add($"You have {unread} {(unread == 1 ? "link" : "links")} still unread.");
        }

        // Named rather than counted: "3 sources found nothing" is a statistic, and the whole
        // value of mentioning it is knowing which one to go and look at.
        if (quiet.Count > 0)
        {
            paragraphs.Add(
                quiet.Count == 1
                    ? $"One source found nothing all week: {quiet[0]}. It may have moved or broken."
                    : $"These sources found nothing all week: {string.Join(", ", quiet)}. They may have moved or broken.");
        }

        return Compose(
            "digest",
            to,
            "Your week on Linkbelli",
            "Your week",
            [.. paragraphs],
            ("Open Linkbelli", publicUrl.TrimEnd('/')));
    }
}
