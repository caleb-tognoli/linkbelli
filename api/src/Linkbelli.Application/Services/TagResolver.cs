using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>
/// Turns normalized tag names into the globally deduplicated <see cref="Tag"/> rows they refer
/// to, creating any that don't exist yet. Shared by playlist tags and item tags so the
/// get-or-create race is handled in one place rather than two.
/// </summary>
public interface ITagResolver
{
    Task<List<Tag>> ResolveAsync(IReadOnlyList<string> names, CancellationToken ct = default);
}

/// <inheritdoc />
public class TagResolver(IAppDbContext db) : ITagResolver
{
    public async Task<List<Tag>> ResolveAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (names.Count == 0)
        {
            return [];
        }

        var resolved = await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync(ct);
        var missing = names.Where(n => resolved.All(t => t.Name != n)).Select(n => new Tag { Name = n }).ToList();
        if (missing.Count == 0)
        {
            return resolved;
        }

        db.Tags.AddRange(missing);
        try
        {
            await db.SaveChangesAsync(ct);
            resolved.AddRange(missing);
            return resolved;
        }
        catch (DbUpdateException)
        {
            // Lost a race creating one or more tags — re-read the authoritative rows.
            foreach (var tag in missing)
            {
                db.Entry(tag).State = EntityState.Detached;
            }

            return await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync(ct);
        }
    }
}
