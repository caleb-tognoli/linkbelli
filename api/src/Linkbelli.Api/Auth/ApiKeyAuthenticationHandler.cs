using System.Security.Claims;
using System.Text.Encodings.Web;
using Linkbelli.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Linkbelli.Api.Auth;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates requests carrying an <c>X-Api-Key</c> header by delegating verification
/// to the Application's <see cref="IApiKeyAuthenticator"/>, then builds the principal.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiKeyAuthenticator authenticator)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyToken.HeaderName, out var header))
        {
            return AuthenticateResult.NoResult(); // let other schemes try
        }

        var principal = await authenticator.AuthenticateAsync(header.ToString(), Context.RequestAborted);
        if (principal is null)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new("auth_method", "apikey"),
        };
        claims.AddRange(principal.Scopes.Select(s => new Claim("scope", s)));

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.Scheme));
    }
}
