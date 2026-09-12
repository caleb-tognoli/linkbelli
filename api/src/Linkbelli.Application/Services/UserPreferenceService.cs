using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class UserPreferenceService(IAppDbContext db) : IUserPreferenceService
{
    public Task<bool> ShowNsfwAsync(Guid? userId, CancellationToken ct = default) =>
        userId is null
            ? Task.FromResult(false)
            : db.Users.Where(u => u.Id == userId.Value).Select(u => u.ShowNsfw).FirstOrDefaultAsync(ct);

    public async Task SetShowNsfwAsync(Guid userId, bool showNsfw, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        user.ShowNsfw = showNsfw;
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> ArchiveLinksAsync(Guid? userId, CancellationToken ct = default) =>
        userId is null
            ? Task.FromResult(false)
            : db.Users.Where(u => u.Id == userId.Value).Select(u => u.ArchiveLinks).FirstOrDefaultAsync(ct);

    public async Task SetArchiveLinksAsync(Guid userId, bool archiveLinks, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        user.ArchiveLinks = archiveLinks;
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> BackupsEnabledAsync(Guid? userId, CancellationToken ct = default) =>
        userId is null
            ? Task.FromResult(false)
            : db.Users.Where(u => u.Id == userId.Value).Select(u => u.BackupsEnabled).FirstOrDefaultAsync(ct);

    public async Task SetBackupsEnabledAsync(Guid userId, bool backupsEnabled, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        user.BackupsEnabled = backupsEnabled;
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> OnboardingDismissedAsync(Guid? userId, CancellationToken ct = default) =>
        userId is null
            ? Task.FromResult(true)
            : db.Users.Where(u => u.Id == userId.Value)
                .Select(u => u.OnboardingDismissedAt != null)
                .FirstOrDefaultAsync(ct);

    public async Task DismissOnboardingAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");

        // Stamped rather than flagged: knowing when somebody decided they were done with it is
        // worth more later than a bare true, and costs the same column.
        user.OnboardingDismissedAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
