using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class AdminService(IAppDbContext db) : IAdminService
{
    public async Task<IReadOnlyList<AdminUserSummary>> SearchUsersAsync(string? q, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(u =>
                (u.UserName != null && u.UserName.ToLower().Contains(needle)) ||
                (u.Email != null && u.Email.ToLower().Contains(needle)));
        }

        return await query
            .OrderBy(u => u.UserName)
            .Take(take)
            .Select(u => new AdminUserSummary(
                u.Id, u.UserName, u.Email,
                db.Playlists.Count(p => p.OwnerId == u.Id),
                db.Sources.Count(s => s.OwnerId == u.Id),
                u.ShowNsfw))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminHostSummary>> ListHostsAsync(string? q, bool? blocked, int? limit, CancellationToken ct = default)
    {
        var take = Math.Clamp(limit ?? 50, 1, 100);
        var query = db.Hosts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(h => h.Hostname.Contains(needle));
        }

        if (blocked is not null)
        {
            query = query.Where(h => h.Blocked == blocked.Value);
        }

        return await query
            .OrderBy(h => h.Hostname)
            .Take(take)
            .Select(h => new AdminHostSummary(h.Id, h.Hostname, h.Blocked, db.Links.Count(l => l.HostId == h.Id)))
            .ToListAsync(ct);
    }

    public async Task<AdminHostSummary> SetHostBlockedAsync(string hostname, bool blocked, CancellationToken ct = default)
    {
        var normalized = hostname.Trim().ToLowerInvariant();
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Hostname == normalized, ct);
        if (host is null)
        {
            host = new Host { Hostname = normalized, Blocked = blocked };
            db.Hosts.Add(host);
        }
        else
        {
            host.Blocked = blocked;
        }

        await db.SaveChangesAsync(ct);
        var linkCount = await db.Links.CountAsync(l => l.HostId == host.Id, ct);
        return new AdminHostSummary(host.Id, host.Hostname, host.Blocked, linkCount);
    }
}
