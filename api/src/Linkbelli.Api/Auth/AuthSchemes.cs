using Microsoft.AspNetCore.Identity;

namespace Linkbelli.Api.Auth;

public static class AuthSchemes
{
    /// <summary>Both schemes — for endpoints usable interactively or programmatically.</summary>
    public static readonly string BearerOrApiKey = $"{IdentityConstants.BearerScheme},{ApiKeyAuthenticationDefaults.Scheme}";
}
