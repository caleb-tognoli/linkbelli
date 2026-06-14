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
}
