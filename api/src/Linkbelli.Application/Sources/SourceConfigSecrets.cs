using Linkbelli.Application.Security;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Manages secret values inside a source's config: encrypts them at rest, redacts them in API
/// responses, and decrypts them just before a fetch.
/// For manual sources: secret detection uses <see cref="ISourceInterpreter.IsSecretConfigKey"/>.
/// For template sources: secret detection uses <see cref="TemplateField.IsSecret"/> on the template's UserFields.
/// </summary>
public sealed class SourceConfigSecrets(IEnumerable<ISourceInterpreter> interpreters, ISecretProtector protector)
{
    /// <summary>Marker returned in responses in place of a stored secret value.</summary>
    public const string Redacted = "***";

    private ISourceInterpreter? Interpreter(SourceType type) =>
        interpreters.FirstOrDefault(i => i.Type == type);

    private bool IsSecret(string key, SourceType type, IEnumerable<TemplateField>? templateFields) =>
        templateFields is not null
            ? TemplateConfigResolver.IsSecretKey(key, templateFields)
            : Interpreter(type)?.IsSecretConfigKey(key) == true;

    /// <summary>
    /// Encrypts secret values for storage. A redacted or blank incoming secret keeps the
    /// previously stored (encrypted) value, so round-tripping a redacted response is safe.
    /// </summary>
    public Dictionary<string, string> Encrypt(
        SourceType type,
        IReadOnlyDictionary<string, string> incoming,
        IReadOnlyDictionary<string, string>? stored,
        IEnumerable<TemplateField>? templateFields = null)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in incoming)
        {
            if (IsSecret(key, type, templateFields))
            {
                if (string.IsNullOrEmpty(value) || value == Redacted)
                {
                    if (stored is not null && stored.TryGetValue(key, out var prior))
                    {
                        result[key] = prior; // preserve the existing secret
                    }

                    continue;
                }

                result[key] = protector.Protect(value);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>Replaces secret values with a redaction marker for API responses.</summary>
    public Dictionary<string, string> Redact(
        SourceType type,
        IReadOnlyDictionary<string, string> stored,
        IEnumerable<TemplateField>? templateFields = null)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in stored)
        {
            result[key] = IsSecret(key, type, templateFields) && !string.IsNullOrEmpty(value)
                ? Redacted
                : value;
        }

        return result;
    }

    /// <summary>Decrypts secret values back to plaintext just before a fetch.</summary>
    public Dictionary<string, string> Decrypt(
        SourceType type,
        IReadOnlyDictionary<string, string> stored,
        IEnumerable<TemplateField>? templateFields = null)
    {
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in stored)
        {
            result[key] = IsSecret(key, type, templateFields) && !string.IsNullOrEmpty(value)
                ? protector.Unprotect(value)
                : value;
        }

        return result;
    }
}
