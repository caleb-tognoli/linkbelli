namespace Linkbelli.Application.Security;

/// <summary>
/// Protects small secret strings at rest (e.g. source-config HTTP header values).
/// Implemented in Infrastructure over ASP.NET Core Data Protection.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
