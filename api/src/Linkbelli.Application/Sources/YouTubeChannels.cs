using System.Text.RegularExpressions;
using Linkbelli.Application.Common;
using Linkbelli.Application.Http;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Turns whatever somebody has of a YouTube channel into the id its feed is addressed by.
/// </summary>
/// <remarks>
/// The template used to ask for the channel id and say "find it in the page source, or paste a
/// handle URL into a converter" — which is an instruction to go and use a different website, in
/// the middle of the simplest template the product ships. What people actually have is the
/// address in their browser bar: <c>youtube.com/@handle</c>, or a <c>/channel/UC…</c> link, or
/// the handle on its own.
///
/// The lookup is a fetch of the channel's own page, with the URL built here from a validated
/// handle — never a URL somebody supplied — so this cannot be pointed at anything else.
/// </remarks>
public interface IYouTubeChannelResolver
{
    /// <summary>
    /// The <c>UC…</c> id for a handle, a channel URL, or an id already.
    /// </summary>
    /// <exception cref="ValidationException">When it is none of those, or cannot be looked up.</exception>
    Task<string> ResolveAsync(string value, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class YouTubeChannelResolver(IHttpClientFactory httpClientFactory)
    : IYouTubeChannelResolver
{
    /// <summary>What the field is called, for the message when nothing can be made of it.</summary>
    private const string Field = "channelId";

    [GeneratedRegex(@"^UC[A-Za-z0-9_-]{22}$")]
    private static partial Regex ChannelId();

    /// <summary>YouTube's own rules: 3–30 of letters, digits, underscore, hyphen and dot.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,30}$")]
    private static partial Regex Handle();

    /// <summary>The id as the channel page states it, in its canonical link or its metadata.</summary>
    [GeneratedRegex(@"(?:""(?:channelId|externalId)""\s*:\s*""|/channel/)(UC[A-Za-z0-9_-]{22})")]
    private static partial Regex IdInPage();

    public async Task<string> ResolveAsync(string value, CancellationToken ct = default)
    {
        var input = value.Trim();
        if (input.Length == 0)
        {
            throw new ValidationException(Field, "A channel handle, address or id is required.");
        }

        if (ChannelId().IsMatch(input))
        {
            return input;
        }

        // A /channel/UC… address carries the answer already; no lookup needed.
        var fromUrl = IdInPage().Match(input);
        if (fromUrl.Success)
        {
            return fromUrl.Groups[1].Value;
        }

        var handle = HandleIn(input)
            ?? throw new ValidationException(
                Field,
                "Use the channel's @handle, the address of its page, or its UC… id.");

        return await LookUpAsync(handle, ct);
    }

    /// <summary>The handle in a youtube.com address, or a bare one. Null when it is neither.</summary>
    private static string? HandleIn(string input)
    {
        var candidate = input;

        if (input.Contains("youtube.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(
                    input.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? input : "https://" + input,
                    UriKind.Absolute,
                    out var uri))
            {
                return null;
            }

            // "/@handle", and the older "/c/name" and "/user/name" forms, which the channel page
            // answers for just the same. Trailing segments — /videos, /streams — are what the
            // address bar actually holds when somebody copies it.
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            candidate = segments switch
            {
                ["c" or "user", var name, ..] => name,
                [var first, ..] => first,
                _ => string.Empty,
            };
        }

        candidate = candidate.TrimStart('@');

        return Handle().IsMatch(candidate) ? candidate : null;
    }

    private async Task<string> LookUpAsync(string handle, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(EnrichmentHttpClient.Name);

        // Built here, from a handle that has already been checked against YouTube's own character
        // rules, so the only host this can ever reach is youtube.com.
        var url = $"https://www.youtube.com/@{Uri.EscapeDataString(handle)}";

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ValidationException(
                Field, "Could not reach YouTube to look that channel up. Try again, or paste the UC… id.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ValidationException(
                Field,
                response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? $"YouTube has no channel at @{handle}."
                    : "Could not look that channel up just now. Try again, or paste the UC… id.");
        }

        var html = await response.Content.ReadAsStringAsync(ct);
        var match = IdInPage().Match(html);

        return match.Success
            ? match.Groups[1].Value
            : throw new ValidationException(
                Field, $"Found @{handle} but not its id. Paste the UC… id from the channel's address.");
    }
}
