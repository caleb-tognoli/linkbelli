using System.Text;

namespace Linkbelli.Core.Search;

/// <summary>What was in the search box once the operators were taken out of it.</summary>
/// <param name="Text">The words left over, to match against. Null when nothing was left.</param>
/// <param name="Host">From <c>site:</c>.</param>
/// <param name="ItemTags">From <c>tag:</c>, which may appear more than once.</param>
/// <param name="Status">From <c>is:read</c> / <c>is:unread</c>.</param>
/// <param name="Broken">From <c>is:broken</c>.</param>
/// <param name="Kind">From <c>kind:</c>.</param>
/// <param name="MinScore">From <c>score:&gt;80</c>.</param>
/// <param name="MaxMinutes">From <c>under:10</c>.</param>
/// <param name="Highlighted">From <c>is:highlighted</c>.</param>
public record ParsedSearch(
    string? Text,
    string? Host,
    IReadOnlyList<string> ItemTags,
    string? Status,
    bool? Broken,
    string? Kind,
    int? MinScore,
    int? MaxMinutes,
    bool Highlighted = false);

/// <summary>
/// Turns what somebody typed into the filters the search already had.
/// </summary>
/// <remarks>
/// Every one of these was reachable only as a chip on the search page — so the filters existed,
/// were exercised on every search, and could not be typed, shared as a URL somebody else could
/// read, or saved as a sentence. <c>site:bbc.co.uk under:10 is:unread</c> is how people expect to
/// ask this, and the vocabulary is already there to answer it.
///
/// Deliberately narrow. Quoted phrases and <c>-exclusion</c> are **not** handled here: Postgres's
/// <c>websearch_to_tsquery</c> already understands both, so the parser's job is to leave them
/// alone rather than to reimplement them badly. An operator inside quotes is part of the phrase,
/// and an unrecognised <c>word:value</c> stays in the text — a colon appears in prose and in URLs
/// far more often than it introduces an operator nobody defined.
/// </remarks>
public static class SearchOperators
{
    /// <summary>Guards a pathological input; nobody types more than a handful of these.</summary>
    private const int MaxOperators = 24;

    public static ParsedSearch Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ParsedSearch(null, null, [], null, null, null, null, null);
        }

        string? host = null;
        var itemTags = new List<string>();
        string? status = null;
        bool? broken = null;
        string? kind = null;
        int? minScore = null;
        int? maxMinutes = null;
        var highlighted = false;

        var text = new StringBuilder();
        var found = 0;

        foreach (var token in Tokenize(raw))
        {
            // A quoted token is a phrase, whatever it happens to contain.
            if (found >= MaxOperators || token.Quoted || !Split(token.Value, out var name, out var value))
            {
                Append(text, token);
                continue;
            }

            var handled = true;
            switch (name)
            {
                case "site":
                case "host":
                    host = Hostname(value);
                    break;

                case "tag":
                    if (value.Length > 0) itemTags.Add(value.ToLowerInvariant());
                    break;

                case "is":
                    switch (value.ToLowerInvariant())
                    {
                        case "read":
                        case "watched":
                        case "done":
                            status = "watched";
                            break;
                        case "unread":
                        case "unwatched":
                            status = "unwatched";
                            break;
                        case "broken":
                            broken = true;
                            break;
                        case "ok":
                        case "alive":
                            broken = false;
                            break;
                        case "highlighted":
                        case "marked":
                            highlighted = true;
                            break;
                        default:
                            handled = false;
                            break;
                    }

                    break;

                case "kind":
                case "type":
                    kind = value.ToLowerInvariant();
                    break;

                case "score":
                    minScore = Threshold(value);
                    handled = minScore is not null;
                    break;

                case "under":
                case "minutes":
                    maxMinutes = Minutes(value);
                    handled = maxMinutes is not null;
                    break;

                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                found++;
            }
            else
            {
                // Not one we know. It stays in the text rather than being silently dropped —
                // "ratio:1" in a title is a search term, not a broken filter.
                Append(text, token);
            }
        }

        var left = text.ToString().Trim();

        return new ParsedSearch(
            left.Length == 0 ? null : left,
            host,
            itemTags,
            status,
            broken,
            kind,
            minScore,
            maxMinutes,
            highlighted);
    }

    /// <summary>An operator is <c>name:value</c>, with a name of plain letters.</summary>
    private static bool Split(string token, out string name, out string value)
    {
        name = string.Empty;
        value = string.Empty;

        var colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1)
        {
            return false;
        }

        var candidate = token[..colon];
        foreach (var c in candidate)
        {
            if (!char.IsAsciiLetter(c))
            {
                // "https://…" lands here, which is the point: an address is not an operator.
                return false;
            }
        }

        name = candidate.ToLowerInvariant();
        value = token[(colon + 1)..].Trim('"');
        return true;
    }

    /// <summary>A bare hostname, however the address was written.</summary>
    private static string? Hostname(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            return null;
        }

        // Pasting a whole URL after site: is the obvious mistake to be forgiving about.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IdnHost.Length > 0)
        {
            return uri.IdnHost;
        }

        return trimmed.TrimStart('/').Split('/')[0];
    }

    /// <summary>
    /// <c>score:&gt;80</c>, <c>score:&gt;=80</c> and <c>score:80</c> all mean "at least".
    /// </summary>
    /// <remarks>
    /// Only a lower bound, because that is the only one the search has. Accepting
    /// <c>score:&lt;20</c> and quietly treating it as a minimum would be worse than not
    /// accepting it, so it falls through and stays in the text.
    /// </remarks>
    private static int? Threshold(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith(">=", StringComparison.Ordinal)) trimmed = trimmed[2..];
        else if (trimmed.StartsWith('>')) trimmed = trimmed[1..];

        return int.TryParse(trimmed, out var score) && score is >= 0 and <= 100 ? score : null;
    }

    /// <summary><c>under:10</c>, <c>under:10m</c>, <c>under:&lt;10</c>.</summary>
    private static int? Minutes(string value)
    {
        var trimmed = value.Trim().TrimStart('<', '=').TrimEnd('m', 'i', 'n', 's');

        return int.TryParse(trimmed, out var minutes) && minutes > 0 ? minutes : null;
    }

    private static void Append(StringBuilder text, Token token)
    {
        if (text.Length > 0) text.Append(' ');

        if (token.Quoted) text.Append('"').Append(token.Value).Append('"');
        else text.Append(token.Value);
    }

    private readonly record struct Token(string Value, bool Quoted);

    /// <summary>
    /// Splits on whitespace, keeping a quoted run together.
    /// </summary>
    /// <remarks>
    /// An unterminated quote takes the rest of the string, which is what somebody halfway through
    /// typing one has — treating it as an error would make the search flicker as they type.
    /// </remarks>
    private static IEnumerable<Token> Tokenize(string raw)
    {
        var current = new StringBuilder();
        var quoted = false;

        foreach (var c in raw)
        {
            if (c == '"')
            {
                if (quoted)
                {
                    yield return new Token(current.ToString(), Quoted: true);
                    current.Clear();
                    quoted = false;
                }
                else
                {
                    if (current.Length > 0)
                    {
                        yield return new Token(current.ToString(), Quoted: false);
                        current.Clear();
                    }

                    quoted = true;
                }

                continue;
            }

            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    yield return new Token(current.ToString(), Quoted: false);
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            yield return new Token(current.ToString(), quoted);
        }
    }
}
