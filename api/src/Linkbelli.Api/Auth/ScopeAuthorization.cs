using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Auth;

/// <summary>Requires the authenticated principal to hold a given API-key scope.</summary>
public sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string Scope { get; } = scope;
}

/// <summary>
/// Enforces <see cref="ScopeRequirement"/>. Scopes only constrain API keys: bearer principals
/// pass unconditionally, and a key with no scopes is treated as unrestricted (full access).
/// </summary>
public sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        var user = context.User;
        if (user.FindFirst("auth_method")?.Value != "apikey")
        {
            context.Succeed(requirement); // interactive bearer = full access
            return Task.CompletedTask;
        }

        var scopes = user.FindAll("scope").Select(c => c.Value).ToList();
        if (scopes.Count == 0 || scopes.Contains(requirement.Scope))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
