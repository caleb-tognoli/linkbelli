using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// One user's settings, read once per request.
/// </summary>
/// <remarks>
/// Each of these used to be its own query against the same row. GET /me asked four times, and
/// the web app's layout calls it on every server-rendered navigation — while ShowNsfw is read
/// again by nearly every listing, search and discovery path, adding a round trip to each. They
/// come back together now, and the result is held for the lifetime of the request, which is what
/// the scoped registration already implies.
/// </remarks>
public class UserPreferenceService(IAppDbContext db) : IUserPreferenceService
{
    /// <summary>What an unknown or anonymous caller gets. Onboarding is "done" so nothing offers it.</summary>
    private static readonly UserPreferences Anonymous =
        new(ShowNsfw: false, ArchiveLinks: false, BackupsEnabled: false, OnboardingDismissed: true);

    private Guid? _cachedFor;
    private UserPreferences? _cached;

    public async Task<UserPreferences> GetAsync(Guid? userId, CancellationToken ct = default)
    {
        if (userId is not { } id)
        {
            return Anonymous;
        }

        if (_cached is not null && _cachedFor == id)
        {
            return _cached;
        }

        var row = await db.Users
            .Where(u => u.Id == id)
            .Select(u => new UserPreferences(
                u.ShowNsfw, u.ArchiveLinks, u.BackupsEnabled, u.OnboardingDismissedAt != null,
                u.EmailConfirmed))
            .FirstOrDefaultAsync(ct);

        // A missing user answers the same as an anonymous one rather than throwing: these are
        // read on paths that tolerate not knowing who is asking.
        _cached = row ?? Anonymous;
        _cachedFor = id;

        return _cached;
    }

    public async Task<bool> ShowNsfwAsync(Guid? userId, CancellationToken ct = default) =>
        (await GetAsync(userId, ct)).ShowNsfw;

    public async Task<bool> ArchiveLinksAsync(Guid? userId, CancellationToken ct = default) =>
        (await GetAsync(userId, ct)).ArchiveLinks;

    public async Task<bool> BackupsEnabledAsync(Guid? userId, CancellationToken ct = default) =>
        (await GetAsync(userId, ct)).BackupsEnabled;

    public async Task<bool> OnboardingDismissedAsync(Guid? userId, CancellationToken ct = default) =>
        (await GetAsync(userId, ct)).OnboardingDismissed;

    public Task SetShowNsfwAsync(Guid userId, bool showNsfw, CancellationToken ct = default) =>
        UpdateAsync(userId, user => user.ShowNsfw = showNsfw, ct);

    public Task SetArchiveLinksAsync(Guid userId, bool archiveLinks, CancellationToken ct = default) =>
        UpdateAsync(userId, user => user.ArchiveLinks = archiveLinks, ct);

    public Task SetBackupsEnabledAsync(Guid userId, bool backupsEnabled, CancellationToken ct = default) =>
        UpdateAsync(userId, user => user.BackupsEnabled = backupsEnabled, ct);

    public Task DismissOnboardingAsync(Guid userId, CancellationToken ct = default) =>
        // Stamped rather than flagged: knowing when somebody decided they were done with it is
        // worth more later than a bare true, and costs the same column.
        UpdateAsync(userId, user => user.OnboardingDismissedAt ??= DateTimeOffset.UtcNow, ct);

    private async Task UpdateAsync(Guid userId, Action<Identity.ApplicationUser> change, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        change(user);
        await db.SaveChangesAsync(ct);

        // Anything read earlier in this request is now stale, and the response is usually built
        // from a read that follows the write.
        _cached = null;
        _cachedFor = null;
    }
}
