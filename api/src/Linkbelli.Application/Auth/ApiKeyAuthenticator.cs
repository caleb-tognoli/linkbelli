using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Auth;

public class ApiKeyAuthenticator(IAppDbContext db) : IApiKeyAuthenticator
{
    private static readonly TimeSpan LastUsedDebounce = TimeSpan.FromMinutes(5);

    public async Task<ApiKeyPrincipal?> AuthenticateAsync(string? headerValue, CancellationToken ct = default)
    {
        if (!ApiKeyToken.TryParse(headerValue, out var publicId, out var secret))
        {
            return null;
        }

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == publicId, ct);
        if (key is null)
        {
            return null;
        }

        if (key.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        if (!ApiKeyToken.FixedTimeEquals(ApiKeyToken.Hash(secret), key.Hash))
        {
            return null;
        }

        // Debounced so we don't write on every authenticated request.
        if (key.LastUsedAt is null || key.LastUsedAt < DateTimeOffset.UtcNow - LastUsedDebounce)
        {
            key.LastUsedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new ApiKeyPrincipal(key.UserId, key.Scopes);
    }
}
