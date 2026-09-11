namespace Linkbelli.Application.Auth;

/// <summary>The verified identity behind a valid API key.</summary>
/// <summary>
/// Who an API key speaks for. <paramref name="Roles"/> is only populated when the key carries an
/// admin scope — an ordinary key never needs them, and looking them up on every request would
/// put a query on the hot path for nothing.
/// </summary>
public record ApiKeyPrincipal(Guid UserId, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles);

public interface IApiKeyAuthenticator
{
    /// <summary>Verifies an X-Api-Key header value; returns the principal, or null if invalid/expired/unknown.</summary>
    Task<ApiKeyPrincipal?> AuthenticateAsync(string? headerValue, CancellationToken ct = default);
}
