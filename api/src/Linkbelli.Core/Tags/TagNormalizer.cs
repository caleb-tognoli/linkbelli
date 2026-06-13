namespace Linkbelli.Core.Tags;

/// <summary>
/// Normalizes free-text tag input so tags dedupe and search consistently: trim, lowercase,
/// collapse internal whitespace, drop empties/dupes, cap length and count. Pure + testable.
/// </summary>
public static class TagNormalizer
{
    public const int MaxLength = 50;
    public const int MaxPerPlaylist = 25;

    /// <summary>One tag → its normalized form (empty string if it normalizes to nothing).</summary>
    public static string NormalizeOne(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var collapsed = string.Join(' ', raw.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > MaxLength ? collapsed[..MaxLength] : collapsed;
    }

    /// <summary>A set of raw tags → normalized, de-duplicated, order-preserving, capped list.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? raw)
    {
        if (raw is null)
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var candidate in raw)
        {
            var normalized = NormalizeOne(candidate);
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
            if (result.Count >= MaxPerPlaylist)
            {
                break;
            }
        }

        return result;
    }
}
