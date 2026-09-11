using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
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

        return new ApiKeyPrincipal(key.UserId, key.Scopes, await RolesAsync(key, ct));
    }

    /// <summary>
    /// The owner's roles, but only for a key that was explicitly granted an admin scope.
    /// </summary>
    /// <remarks>
    /// Deliberately not for unrestricted keys. A key with no scopes is unrestricted over its
    /// owner's own data; letting that quietly carry admin power over the whole instance would
    /// make every general-purpose key an instance-wide credential. Admin access by key is opt-in,
    /// per key — and the scope still only opens the door: the owner has to be an admin.
    /// </remarks>
    private async Task<IReadOnlyList<string>> RolesAsync(ApiKey key, CancellationToken ct)
    {
        var wantsAdmin = key.Scopes.Any(s => s.StartsWith("admin:", StringComparison.Ordinal));
        if (!wantsAdmin)
        {
            return [];
        }

        return await db.UserRoles
            .Where(ur => ur.UserId == key.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(ct);
    }
}
