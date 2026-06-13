using System.Security.Claims;
using System.Text.Encodings.Web;
using Linkbelli.Core.Auth;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Linkbelli.Api.Auth;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// Authenticates requests carrying an <c>X-Api-Key</c> header. Looks the key up
/// by its public prefix, then verifies the secret hash in constant time.
/// </summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    LinkbelliDbContext db)
    : AuthenticationHandler<ApiKeyAuthenticationOptions>(options, logger, encoder)
{
    private static readonly TimeSpan LastUsedDebounce = TimeSpan.FromMinutes(5);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyToken.HeaderName, out var header))
        {
            return AuthenticateResult.NoResult(); // let other schemes try
        }

        if (!ApiKeyToken.TryParse(header.ToString(), out var publicId, out var secret))
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == publicId);
        if (key is null)
        {
            return AuthenticateResult.Fail("Unknown API key.");
        }

        if (key.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return AuthenticateResult.Fail("Expired API key.");
        }

        if (!ApiKeyToken.FixedTimeEquals(ApiKeyToken.Hash(secret), key.Hash))
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        // Debounced so we don't write on every authenticated request.
        if (key.LastUsedAt is null || key.LastUsedAt < DateTimeOffset.UtcNow - LastUsedDebounce)
        {
            key.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.UserId.ToString()),
            new("auth_method", "apikey"),
        };
        claims.AddRange(key.Scopes.Select(s => new Claim("scope", s)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme));
    }
}
