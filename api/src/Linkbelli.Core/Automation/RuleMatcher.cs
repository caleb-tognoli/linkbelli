using System.Text.RegularExpressions;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;

namespace Linkbelli.Core.Automation;

/// <summary>What an automation rule is shown about an item, and nothing else.</summary>
/// <remarks>
/// A plain record rather than the entities themselves, so the matching rules can be tested
/// without a database and can't accidentally come to depend on anything else about an item.
/// </remarks>
public record RuleCandidate(
    Guid PlaylistId,
    string Url,
    string Host,
    string? Title,
    ContentKind Kind);

/// <summary>
/// One rule with its patterns compiled. Built once per pass, because a rule is tested against
/// every arriving item and compiling two regexes per item would be most of the work.
/// </summary>
public sealed class CompiledRule
{
    private readonly Regex? _title;
    private readonly Regex? _url;

    public AutomationRule Rule { get; }

    public CompiledRule(AutomationRule rule)
    {
        Rule = rule;
        _title = Build(rule.TitlePattern);
        _url = Build(rule.UrlPattern);
    }

    /// <summary>Longest pattern accepted, matching the limit on source filters.</summary>
    public const int MaxPatternLength = 200;

    /// <summary>
    /// Compiles one user-supplied pattern, or returns null for none. Throws
    /// <see cref="ArgumentException"/> for one that won't parse, so a save can refuse it rather
    /// than a rule failing silently every time it runs.
    /// </summary>
    public static Regex? Build(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        if (pattern.Length > MaxPatternLength)
        {
            throw new ArgumentException($"Pattern is longer than {MaxPatternLength} characters.", nameof(pattern));
        }

        const RegexOptions common = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        try
        {
            return new Regex(pattern, common | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // Lookarounds and backreferences: the linear engine has neither, so those run on the
            // backtracking one behind a timeout instead.
            return new Regex(pattern, common, TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    /// Whether this rule applies. Every condition set on the rule must hold — conditions are
    /// AND, because a rule that fires when *any* of its conditions matches is nearly impossible
    /// to predict once it has more than one.
    /// </summary>
    public bool Matches(RuleCandidate candidate)
    {
        if (!Rule.Enabled)
        {
            return false;
        }

        if (Rule.PlaylistId is { } playlistId && playlistId != candidate.PlaylistId)
        {
            return false;
        }

        if (Rule.Host is { Length: > 0 } host
            && !host.Equals(candidate.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Rule.Kind is { } kind && kind != candidate.Kind)
        {
            return false;
        }

        if (_title is not null && !IsMatch(_title, candidate.Title))
        {
            return false;
        }

        if (_url is not null && !IsMatch(_url, candidate.Url))
        {
            return false;
        }

        // A rule with no conditions at all matches everything, which is a legitimate thing to
        // want ("tag everything that arrives") and is exactly what the person wrote.
        return true;
    }

    /// <summary>
    /// A pattern that times out counts as no match. One pathological title shouldn't stop every
    /// other item that arrived with it from being filed.
    /// </summary>
    private static bool IsMatch(Regex regex, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
