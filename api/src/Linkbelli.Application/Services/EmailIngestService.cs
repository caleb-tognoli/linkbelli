using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Url;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Application.Services;

/// <summary>
/// Saving a link by emailing it.
/// </summary>
/// <remarks>
/// A second door onto the webhook source built for pushes, not a second kind of source: the
/// difference between the two is only how the payload arrives, and giving them separate ingestion
/// paths is how two things that should behave identically quietly stop doing so.
///
/// The email itself is parsed elsewhere — see <c>email-worker/</c> in the repository — because
/// receiving mail is a job for something that already receives mail. What arrives here is the
/// parts of a message that matter.
/// </remarks>
public interface IEmailIngestService
{
    Task<WebhookPushResponse> IngestAsync(
        string token, EmailIngestRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class EmailIngestService(
    IAppDbContext db,
    IWebhookIngestService webhooks,
    SourceConfigSecrets secrets,
    ILogger<EmailIngestService> logger) : IEmailIngestService
{
    /// <summary>
    /// The config key naming who may email this source, as a comma-separated list.
    /// </summary>
    /// <remarks>
    /// Absent means the account's own address and nothing else. An inbox address is not a secret
    /// the way a POST token is — it ends up in headers, in forwards, in other people's sent
    /// folders — so a leaked one must not let a stranger write into somebody's playlist.
    /// </remarks>
    public const string AllowedSendersKey = "allowedSenders";

    /// <summary>How much of a message body to read for links.</summary>
    /// <remarks>
    /// A newsletter can be enormous, and the addresses worth having are near the top. This is a
    /// bound on work rather than a judgement about content; the extractor caps the count anyway.
    /// </remarks>
    public const int MaxBodyChars = 200_000;

    public async Task<WebhookPushResponse> IngestAsync(
        string token, EmailIngestRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.From))
        {
            throw new ValidationException("from", "A sender is required.");
        }

        // Resolved by decrypting each candidate, because the stored token is encrypted and there
        // is nothing to match on in the database. The same walk the webhook push does, including
        // the fixed-time comparison: this endpoint is anonymous and the token is the credential.
        var config = await ResolveConfigAsync(token, ct)
            ?? throw new NotFoundException("Source not found.");

        if (!await IsAllowedAsync(config, request.From, ct))
        {
            // Deliberately not distinguished from an unknown token. A sender learning that the
            // address exists but they are not on the list is an invitation to try another one.
            logger.LogInformation("An email to a source came from an address that is not allowed.");
            throw new NotFoundException("Source not found.");
        }

        var links = ExtractLinks(request);
        if (links.Count == 0)
        {
            throw new ValidationException("body", "That message had no links in it.");
        }

        // The subject becomes a title only when there is exactly one link. On a message with
        // several, the subject describes the message rather than any one of them, and stamping it
        // on all of them would be wrong about every single one.
        var title = links.Count == 1 ? Subject(request) : null;

        return await webhooks.PushAsync(
            token,
            new WebhookPushRequest([.. links.Select(url => new PushedLinkDto(url, title))]),
            ct);
    }

    /// <summary>
    /// Every address in the message, in the order they appeared.
    /// </summary>
    /// <remarks>
    /// The subject is read first and deliberately: forwarding an article with its address in the
    /// subject line is a normal thing to do, and it is the one place where a single URL is
    /// unambiguously the point of the message.
    /// </remarks>
    private static List<string> ExtractLinks(EmailIngestRequest request)
    {
        var found = new List<string>();

        foreach (var candidate in new[] { request.Subject, Truncate(request.Text), Truncate(request.Html) })
        {
            foreach (var url in UrlExtractor.Extract(candidate))
            {
                // Case-sensitively distinct URLs are still the same link to the deduplication
                // that follows, but there is no reason to send the same string twice.
                if (!found.Contains(url, StringComparer.OrdinalIgnoreCase)) found.Add(url);
            }
        }

        return found;
    }

    private static string? Truncate(string? body) =>
        body is null || body.Length <= MaxBodyChars ? body : body[..MaxBodyChars];

    /// <summary>The subject, trimmed, or null when there isn't one worth using.</summary>
    private static string? Subject(EmailIngestRequest request) =>
        string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim();

    /// <summary>The decrypted config of the source this token addresses, with its owner.</summary>
    private async Task<(Guid OwnerId, Dictionary<string, string> Config)?> ResolveConfigAsync(
        string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var candidates = await db.Sources
            .Where(s => s.Type == SourceType.Webhook)
            .Select(s => new { s.OwnerId, s.Config })
            .ToListAsync(ct);

        var wanted = Encoding.UTF8.GetBytes(token);

        foreach (var candidate in candidates)
        {
            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(candidate.Config) ?? [];
            var config = secrets.Decrypt(SourceType.Webhook, stored);

            if (config.TryGetValue(WebhookSourceInterpreter.TokenKey, out var storedToken)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedToken), wanted))
            {
                return (candidate.OwnerId, config);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this sender may write to this source.
    /// </summary>
    /// <remarks>
    /// The owner's own address always may — the common case is somebody mailing themselves a
    /// link — and anybody the owner listed. Matched on the address only, so a display name in
    /// front of it changes nothing.
    /// </remarks>
    private async Task<bool> IsAllowedAsync(
        (Guid OwnerId, Dictionary<string, string> Config) source, string from, CancellationToken ct)
    {
        var sender = NormalizeAddress(from);
        if (sender is null) return false;

        if (source.Config.TryGetValue(AllowedSendersKey, out var allowed)
            && !string.IsNullOrWhiteSpace(allowed))
        {
            return allowed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeAddress)
                .Any(a => a is not null && a.Equals(sender, StringComparison.OrdinalIgnoreCase));
        }

        var ownerEmail = await db.Users
            .Where(u => u.Id == source.OwnerId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        return ownerEmail is not null
            && ownerEmail.Equals(sender, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The bare address out of whatever a mail client put in the From header.
    /// </summary>
    /// <remarks>
    /// "Caleb &lt;a@b.com&gt;" and "a@b.com" are the same sender, and every client disagrees
    /// about which of the two it sends.
    /// </remarks>
    public static string? NormalizeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var trimmed = value.Trim();

        var open = trimmed.LastIndexOf('<');
        var close = trimmed.LastIndexOf('>');
        if (open >= 0 && close > open)
        {
            trimmed = trimmed[(open + 1)..close].Trim();
        }

        return trimmed.Contains('@') ? trimmed : null;
    }
}
