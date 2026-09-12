using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Linkbelli.Application.Auth;

/// <summary>How an instance decides whether it will take new accounts.</summary>
public enum RegistrationMode
{
    /// <summary>Anyone who can reach the address can sign up. The default, and what it has always done.</summary>
    Open = 0,

    /// <summary>
    /// Only the first account. A single-person instance stops being open the moment it has
    /// somebody in it, which is what most self-hosters actually want and previously had to do
    /// with a reverse-proxy rule.
    /// </summary>
    FirstUserOnly = 1,

    /// <summary>Nobody. Accounts are created by restarting with this off, or not at all.</summary>
    Closed = 2,
}

/// <summary>Whether this instance is accepting new accounts.</summary>
public interface IRegistrationPolicy
{
    Task<bool> IsOpenAsync(CancellationToken ct = default);

    /// <summary>The configured mode, so the sign-up page can say so before anyone tries.</summary>
    RegistrationMode Mode { get; }
}

/// <inheritdoc />
/// <remarks>
/// Read from configuration rather than stored, so closing an instance is an operational change
/// and cannot be undone by anybody who gets into the admin console.
/// </remarks>
public sealed class RegistrationPolicy(IAppDbContext db, IConfiguration configuration) : IRegistrationPolicy
{
    public RegistrationMode Mode { get; } =
        Enum.TryParse<RegistrationMode>(configuration["Registration:Mode"], ignoreCase: true, out var mode)
            ? mode
            : RegistrationMode.Open;

    public async Task<bool> IsOpenAsync(CancellationToken ct = default) => Mode switch
    {
        RegistrationMode.Open => true,
        RegistrationMode.Closed => false,
        // Counted rather than remembered: an instance whose only account was deleted should be
        // able to take a new one, and a flag written at first sign-up could not know that.
        RegistrationMode.FirstUserOnly => !await db.Users.AnyAsync(ct),
        _ => true,
    };
}
