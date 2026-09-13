using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Identity;

/// <summary>
/// What an administrator can do about an account.
/// </summary>
/// <remarks>
/// The moderation half of the admin surface is well built — reports, the host blocklist, NSFW
/// overrides, the audit trail. The account half was one read and one quota setting: an admin
/// could see every user and could do nothing whatever about them. No suspend, no delete, no
/// promote or demote.
///
/// Granting admin meant editing configuration and restarting, so an instance with two admins
/// could not add a third without a deploy, or remove one without a deploy either. That path
/// stays, as the bootstrap: a fresh instance with nobody in the database still needs a way in.
/// </remarks>
public interface IAdminUserService
{
    /// <summary>
    /// Blocks sign-in and takes the public content down, keeping everything.
    /// </summary>
    /// <remarks>
    /// Kept apart from deletion because the two mean opposite things about whose decision it was,
    /// and reversing a suspension has to put back exactly what was published — which is why the
    /// visibility is remembered rather than guessed at.
    /// </remarks>
    Task SuspendAsync(Guid actorId, Guid userId, CancellationToken ct = default);

    /// <summary>Lifts a suspension, restoring what was published before it.</summary>
    Task ReinstateAsync(Guid actorId, Guid userId, CancellationToken ct = default);

    /// <summary>Adds or removes the admin role. Returns whether they have it afterwards.</summary>
    Task<bool> SetAdminAsync(Guid actorId, Guid userId, bool admin, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AdminUserService(
    IAppDbContext db,
    UserManager<ApplicationUser> users,
    IAuditLog audit) : IAdminUserService
{
    public const string AdminRole = "Admin";

    public async Task SuspendAsync(Guid actorId, Guid userId, CancellationToken ct = default)
    {
        var user = await FindAsync(userId);

        if (user.SuspendedAt is not null)
        {
            return;
        }

        user.SuspendedAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);

        // Every session ends now, or a suspension does nothing for an hour to whoever is already
        // signed in — which is the hour it exists for.
        await users.UpdateSecurityStampAsync(user);
        await AccountVisibility.HideAsync(db, userId, ct);

        await audit.RecordAsync(
            actorId, "admin.user.suspended", "User", userId, asAdmin: true, ct: ct);
    }

    public async Task ReinstateAsync(Guid actorId, Guid userId, CancellationToken ct = default)
    {
        var user = await FindAsync(userId);

        if (user.SuspendedAt is null)
        {
            return;
        }

        user.SuspendedAt = null;
        await users.UpdateAsync(user);
        await AccountVisibility.RestoreAsync(db, userId, ct);

        await audit.RecordAsync(
            actorId, "admin.user.reinstated", "User", userId, asAdmin: true, ct: ct);
    }

    public async Task<bool> SetAdminAsync(
        Guid actorId, Guid userId, bool admin, CancellationToken ct = default)
    {
        var user = await FindAsync(userId);

        if (!admin && actorId == userId)
        {
            // The last-admin problem in its most common form. Locking yourself out of your own
            // instance takes a deploy to undo, and somebody meaning to demote a colleague and
            // clicking their own row is not a far-fetched afternoon.
            throw new ValidationException(
                "userId", "You cannot remove your own administrator access. Ask another admin to.");
        }

        var has = await users.IsInRoleAsync(user, AdminRole);
        if (has == admin)
        {
            return has;
        }

        var result = admin
            ? await users.AddToRoleAsync(user, AdminRole)
            : await users.RemoveFromRoleAsync(user, AdminRole);

        if (!result.Succeeded)
        {
            throw new ConflictException(
                string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // The role is read from the token's claims, so an existing session keeps whatever it was
        // granted at sign-in until it refreshes. Rotating the stamp makes it take effect now,
        // which matters far more for a revocation than for a grant.
        await users.UpdateSecurityStampAsync(user);

        await audit.RecordAsync(
            actorId,
            admin ? "admin.user.promoted" : "admin.user.demoted",
            "User",
            userId,
            asAdmin: true,
            ct: ct);

        return admin;
    }

    private async Task<ApplicationUser> FindAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString())
        ?? throw new NotFoundException("User not found.");
}
