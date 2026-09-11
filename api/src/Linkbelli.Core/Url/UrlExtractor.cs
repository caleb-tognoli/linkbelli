using System.Text.RegularExpressions;

namespace Linkbelli.Core.Url;

/// <summary>
/// Pulls web addresses out of whatever someone pasted.
/// </summary>
/// <remarks>
/// Adding links one at a time, or exporting a file and importing it, were the only two options.
/// What people actually have is a chat log, a list of tabs, an email, or a note — a block of text
/// with addresses in it.
/// </remarks>
public static partial class UrlExtractor
{
    /// <summary>Most addresses accepted from one paste. A paste, not an import.</summary>
    public const int MaxUrls = 200;

    /// <summary>
    /// Characters that end an address when they are the last thing in it. A URL at the end of a
    /// sentence collects the full stop, and markdown or a quoted line wraps it in brackets.
    /// </summary>
    private const string TrailingNoise = ".,;:!?)]}>'\"";

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern { get; }

    /// <summary>
    /// Every distinct address in the text, in the order they appear.
    /// </summary>
    /// <remarks>
    /// Order is kept because a pasted list is usually in a deliberate order, and returning it
    /// shuffled makes the result look like something else happened.
    /// </remarks>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<string>();

        foreach (Match match in UrlPattern.Matches(text))
        {
            var url = Clean(match.Value);
            if (url.Length > 0 && seen.Add(url))
            {
                found.Add(url);

                if (found.Count == MaxUrls)
                {
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Trims the punctuation a URL picks up from the prose around it, while leaving the
    /// punctuation that belongs to it — a closing bracket is only noise if nothing opened it.
    /// </summary>
    private static string Clean(string url)
    {
        while (url.Length > 0 && TrailingNoise.Contains(url[^1]))
        {
            var last = url[^1];

            if (last == ')' && Balanced(url, '(', ')')) break;
            if (last == ']' && Balanced(url, '[', ']')) break;

            url = url[..^1];
        }

        return url;
    }

    /// <summary>Whether the closing character has an opener inside the address itself.</summary>
    private static bool Balanced(string url, char open, char close) =>
        url.Count(c => c == open) >= url.Count(c => c == close);
}
