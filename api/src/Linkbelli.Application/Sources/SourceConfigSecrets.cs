using Linkbelli.Application.Security;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Manages secret values inside a source's config: encrypts them at rest, redacts them in API
/// responses, and decrypts them just before a fetch. Which keys are secret is decided by the
/// matching interpreter (<see cref="ISourceInterpreter.IsSecretConfigKey"/>).
/// </summary>
public sealed class SourceConfigSecrets(IEnumerable<ISourceInterpreter> interpreters, ISecretProtector protector)
{
    /// <summary>Marker returned in responses in place of a stored secret value.</summary>
    public const string Redacted = "***";

    private ISourceInterpreter? Interpreter(SourceType type) =>
        interpreters.FirstOrDefault(i => i.Type == type);

    /// <summary>
    /// Encrypts secret values for storage. A redacted or blank incoming secret keeps the
    /// previously stored (encrypted) value, so round-tripping a redacted response is safe.
    /// </summary>
    public Dictionary<string, string> Encrypt(
        SourceType type,
        IReadOnlyDictionary<string, string> incoming,
        IReadOnlyDictionary<string, string>? stored)
    {
        var interp = Interpreter(type);
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in incoming)
        {
            if (interp?.IsSecretConfigKey(key) == true)
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
    public Dictionary<string, string> Redact(SourceType type, IReadOnlyDictionary<string, string> stored)
    {
        var interp = Interpreter(type);
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in stored)
        {
            result[key] = interp?.IsSecretConfigKey(key) == true && !string.IsNullOrEmpty(value)
                ? Redacted
                : value;
        }

        return result;
    }

    /// <summary>Decrypts secret values back to plaintext just before a fetch.</summary>
    public Dictionary<string, string> Decrypt(SourceType type, IReadOnlyDictionary<string, string> stored)
    {
        var interp = Interpreter(type);
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in stored)
        {
            result[key] = interp?.IsSecretConfigKey(key) == true && !string.IsNullOrEmpty(value)
                ? protector.Unprotect(value)
                : value;
        }

        return result;
    }
}
