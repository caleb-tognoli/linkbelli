using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Sources;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Tags;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class SourceTemplateService(IAppDbContext db) : ISourceTemplateService
{
    public async Task<IReadOnlyList<SourceTemplateSummary>> ListAsync(Guid callerId, CancellationToken ct = default)
    {
        var savedIds = await db.UserSavedTemplates
            .Where(s => s.UserId == callerId)
            .Select(s => s.TemplateId)
            .ToHashSetAsync(ct);

        var templates = await db.SourceTemplates
            .AsNoTracking()
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Where(t =>
                t.OwnerId == callerId ||                              // own
                savedIds.Contains(t.Id) ||                           // saved
                (t.OwnerId == null &&                                 // system (app)
                 (t.Visibility == SourceTemplateVisibility.Public || t.Visibility == SourceTemplateVisibility.Unlisted)))
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return templates
            .Select(t => ToSummary(t, callerId, savedIds))
            .ToList();
    }

    public async Task<IReadOnlyList<SourceTemplateResponse>> ListAllAsync(CancellationToken ct = default)
    {
        var templates = await db.SourceTemplates
            .AsNoTracking()
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .OrderBy(t => t.OwnerId == null ? 0 : 1)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(t => ToResponse(t, ownedByMe: false, savedByMe: false)).ToList();
    }

    public async Task<IReadOnlyList<SourceTemplateSummary>> DiscoverAsync(
        Guid callerId, string? q, string[]? tags, CancellationToken ct = default)
    {
        var savedIds = await db.UserSavedTemplates
            .Where(s => s.UserId == callerId)
            .Select(s => s.TemplateId)
            .ToHashSetAsync(ct);

        var query = db.SourceTemplates
            .AsNoTracking()
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .Where(t =>
                t.OwnerId != callerId && t.OwnerId != null && // not own, not system
                (t.Visibility == SourceTemplateVisibility.Public || t.Visibility == SourceTemplateVisibility.Unlisted));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(needle) ||
                                     (t.Description != null && t.Description.ToLower().Contains(needle)));
        }

        if (tags is { Length: > 0 })
        {
            foreach (var tag in tags)
            {
                var norm = TagNormalizer.NormalizeOne(tag);
                if (norm.Length > 0)
                    query = query.Where(t => t.Tags.Any(tt => tt.Tag!.Name == norm));
            }
        }

        var results = await query.OrderBy(t => t.Name).Take(100).ToListAsync(ct);
        return results.Select(t => ToSummary(t, callerId, savedIds)).ToList();
    }

    public async Task<SourceTemplateResponse> GetAsync(Guid id, Guid callerId, bool isAdmin, CancellationToken ct = default)
    {
        var template = await db.SourceTemplates
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template not found.");

        if (!isAdmin && template.OwnerId != callerId)
        {
            if (template.Visibility == SourceTemplateVisibility.Private)
                throw new NotFoundException("Template not found.");
        }

        var savedByMe = await db.UserSavedTemplates.AnyAsync(s => s.UserId == callerId && s.TemplateId == id, ct);
        return ToResponse(template, template.OwnerId == callerId, savedByMe);
    }

    public async Task<SourceTemplateResponse> CreateAsync(
        Guid callerId, bool isAdmin, CreateSourceTemplateRequest request, CancellationToken ct = default)
    {
        ValidateName(request.Name);

        var tagEntities = await ResolveTagsAsync(TagNormalizer.Normalize(request.Tags ?? []), ct);

        var template = new SourceTemplate
        {
            OwnerId = (request.IsSystem && isAdmin) ? null : callerId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Type = request.Type,
            BaseConfig = new Dictionary<string, string>(request.BaseConfig),
            UserFields = request.UserFields.Select(ToField).ToList(),
            DefaultSchedule = request.DefaultSchedule?.Trim(),
            Visibility = request.Visibility,
        };
        db.SourceTemplates.Add(template);
        await db.SaveChangesAsync(ct); // get template.Id

        foreach (var tag in tagEntities)
            db.TemplateTags.Add(new TemplateTag { TemplateId = template.Id, TagId = tag.Id });

        if (tagEntities.Count > 0)
            await db.SaveChangesAsync(ct);

        return ToResponse(template, ownedByMe: true, savedByMe: false, tagEntities.Select(t => t.Name));
    }

    public async Task<SourceTemplateResponse> UpdateAsync(
        Guid id, Guid callerId, bool isAdmin, UpdateSourceTemplateRequest request, CancellationToken ct = default)
    {
        var template = await db.SourceTemplates
            .Include(t => t.Tags).ThenInclude(tt => tt.Tag)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template not found.");

        if (!isAdmin && template.OwnerId != callerId)
            throw new NotFoundException("Template not found.");

        if (request.Name is not null)
        {
            ValidateName(request.Name);
            template.Name = request.Name.Trim();
        }

        if (request.Description is not null)
            template.Description = request.Description.Trim();

        if (request.BaseConfig is not null)
            template.BaseConfig = new Dictionary<string, string>(request.BaseConfig);

        if (request.UserFields is not null)
            template.UserFields = request.UserFields.Select(ToField).ToList();

        if (request.DefaultSchedule is not null)
            template.DefaultSchedule = request.DefaultSchedule.Trim();

        if (request.Visibility is not null)
            template.Visibility = request.Visibility.Value;

        if (request.Tags is not null)
        {
            var tagEntities = await ResolveTagsAsync(TagNormalizer.Normalize(request.Tags), ct);
            var existing = await db.TemplateTags.Where(tt => tt.TemplateId == id).ToListAsync(ct);
            db.TemplateTags.RemoveRange(existing);
            foreach (var tag in tagEntities)
                db.TemplateTags.Add(new TemplateTag { TemplateId = id, TagId = tag.Id });
        }

        await db.SaveChangesAsync(ct);

        var savedByMe = await db.UserSavedTemplates.AnyAsync(s => s.UserId == callerId && s.TemplateId == id, ct);
        return ToResponse(template, template.OwnerId == callerId, savedByMe);
    }

    public async Task DeleteAsync(Guid id, Guid callerId, bool isAdmin, CancellationToken ct = default)
    {
        var template = await db.SourceTemplates.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Template not found.");

        if (!isAdmin && template.OwnerId != callerId)
            throw new NotFoundException("Template not found.");

        db.SourceTemplates.Remove(template); // soft delete
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveAsync(Guid templateId, Guid userId, CancellationToken ct = default)
    {
        var template = await db.SourceTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException("Template not found.");

        if (template.OwnerId == userId)
            return; // no point saving your own

        if (template.Visibility == SourceTemplateVisibility.Private && template.OwnerId != userId)
            throw new NotFoundException("Template not found.");

        var already = await db.UserSavedTemplates
            .AnyAsync(s => s.UserId == userId && s.TemplateId == templateId, ct);

        if (!already)
        {
            db.UserSavedTemplates.Add(new UserSavedTemplate { UserId = userId, TemplateId = templateId });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UnsaveAsync(Guid templateId, Guid userId, CancellationToken ct = default)
    {
        var row = await db.UserSavedTemplates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.TemplateId == templateId, ct);

        if (row is not null)
        {
            db.UserSavedTemplates.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<List<Tag>> ResolveTagsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        if (names.Count == 0) return [];

        var resolved = await db.Tags.Where(t => names.Contains(t.Name)).ToListAsync(ct);
        var missing = names.Where(n => resolved.All(t => t.Name != n)).Select(n => new Tag { Name = n }).ToList();
        if (missing.Count > 0)
        {
            foreach (var tag in missing) db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);
            resolved.AddRange(missing);
        }
        return resolved;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("name", "Name is required.");
    }

    private static TemplateField ToField(TemplateFieldDto dto) =>
        new(dto.Key, dto.Label, dto.Description, dto.Type, dto.Required, dto.IsSecret);

    private static TemplateFieldDto ToFieldDto(TemplateField f) =>
        new(f.Key, f.Label, f.Description, f.Type, f.Required, f.IsSecret);

    private static SourceTemplateSummary ToSummary(
        SourceTemplate t, Guid callerId, HashSet<Guid> savedIds) => new(
        t.Id, t.OwnerId, t.Name, t.Description, t.Type,
        t.UserFields.Select(ToFieldDto).ToList(),
        t.DefaultSchedule,
        t.Visibility,
        t.Tags.Select(tt => tt.Tag!.Name).OrderBy(n => n).ToList(),
        OwnedByMe: t.OwnerId == callerId,
        SavedByMe: savedIds.Contains(t.Id),
        t.CreationTime);

    private static SourceTemplateResponse ToResponse(
        SourceTemplate t, bool ownedByMe, bool savedByMe,
        IEnumerable<string>? tagNames = null) => new(
        t.Id, t.OwnerId, t.Name, t.Description, t.Type,
        t.BaseConfig,
        t.UserFields.Select(ToFieldDto).ToList(),
        t.DefaultSchedule,
        t.Visibility,
        (tagNames ?? t.Tags.Select(tt => tt.Tag!.Name)).OrderBy(n => n).ToList(),
        ownedByMe, savedByMe,
        t.CreationTime);
}
