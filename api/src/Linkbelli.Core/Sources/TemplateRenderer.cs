using System.Text.RegularExpressions;

namespace Linkbelli.Core.Sources;

/// <summary>
/// Fills a template's <c>{{placeholder}}</c> markers with the values someone supplied. Pure text
/// substitution — the result still goes through the interpreter's own config validation, so a
/// template cannot talk the app into accepting a config it otherwise wouldn't.
/// </summary>
public static partial class TemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Every placeholder a template uses, in the order they first appear.</summary>
    public static IReadOnlyList<string> Placeholders(IEnumerable<string> templateValues)
    {
        var found = new List<string>();

        foreach (var value in templateValues)
        {
            foreach (Match match in PlaceholderPattern().Matches(value))
            {
                var name = match.Groups[1].Value;
                if (!found.Contains(name, StringComparer.Ordinal))
                {
                    found.Add(name);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Substitutes into one value. A placeholder with no supplied value is left as it is, so
    /// <see cref="MissingValues"/> can report it rather than silently producing a broken URL.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        PlaceholderPattern().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : match.Value;
        });

    /// <summary>Substitutes across a whole config.</summary>
    public static Dictionary<string, string> Render(
        IReadOnlyDictionary<string, string> template, IReadOnlyDictionary<string, string> values) =>
        template.ToDictionary(pair => pair.Key, pair => Render(pair.Value, values), StringComparer.Ordinal);

    /// <summary>
    /// Placeholders the supplied values don't cover. Reported up front rather than left to fail
    /// later as a fetch against a URL with "{{channelId}}" still in it.
    /// </summary>
    public static IReadOnlyList<string> MissingValues(
        IReadOnlyDictionary<string, string> template, IReadOnlyDictionary<string, string> values) =>
        Placeholders(template.Values)
            .Where(name => !values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();
}
