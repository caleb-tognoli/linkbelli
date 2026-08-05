using System.Text.RegularExpressions;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Resolves a template's base config into a runnable config by substituting
/// {{key}} placeholders with the source's user-provided values.
/// </summary>
public static partial class TemplateConfigResolver
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();

    public static Dictionary<string, string> Resolve(
        Dictionary<string, string> baseConfig,
        IReadOnlyDictionary<string, string>? userParams)
    {
        return baseConfig.ToDictionary(
            kv => kv.Key,
            kv => PlaceholderRegex().Replace(
                kv.Value,
                m => userParams?.GetValueOrDefault(m.Groups[1].Value) ?? string.Empty));
    }

    /// <summary>
    /// Determines whether a config key is secret according to the template's field definitions.
    /// </summary>
    public static bool IsSecretKey(string key, IEnumerable<TemplateField> userFields) =>
        userFields.Any(f => f.Key == key && f.IsSecret);
}
