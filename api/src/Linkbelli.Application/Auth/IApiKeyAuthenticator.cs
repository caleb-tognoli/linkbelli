namespace Linkbelli.Application.Auth;

/// <summary>The verified identity behind a valid API key.</summary>
public record ApiKeyPrincipal(Guid UserId, IReadOnlyList<string> Scopes);

public interface IApiKeyAuthenticator
{
    /// <summary>Verifies an X-Api-Key header value; returns the principal, or null if invalid/expired/unknown.</summary>
    Task<ApiKeyPrincipal?> AuthenticateAsync(string? headerValue, CancellationToken ct = default);
}
