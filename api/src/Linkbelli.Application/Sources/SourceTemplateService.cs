using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Ready-made source configs. Writing a feed path or a set of CSS selectors is the steepest part
/// of setting a source up, and it is identical work for everyone pointing at the same service.
/// </summary>
public interface ISourceTemplateService
{
    Task<IReadOnlyList<SourceTemplateResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Turns a template plus the person's values into a config ready for the interpreter. Throws
    /// ValidationException naming the fields that are still missing.
    /// </summary>
    Task<(SourceType Type, Dictionary<string, string> Config, string? Schedule)> RenderAsync(
        Guid templateId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);

    /// <summary>Inserts or updates the built-ins, so a fix to one reaches everyone.</summary>
    Task SeedBuiltinsAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public class SourceTemplateService(IAppDbContext db) : ISourceTemplateService
{
    public async Task<IReadOnlyList<SourceTemplateResponse>> ListAsync(CancellationToken ct = default)
    {
        var templates = await db.SourceTemplates
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return templates.Select(Describe).ToList();
    }

    public async Task<(SourceType Type, Dictionary<string, string> Config, string? Schedule)> RenderAsync(
        Guid templateId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        var template = await db.SourceTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new NotFoundException("Template not found.");

        var baseConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(template.BaseConfig)
            ?? throw new InvalidOperationException($"Template '{template.Name}' has an unreadable config.");

        var missing = TemplateRenderer.MissingValues(baseConfig, values);
        if (missing.Count > 0)
        {
            // Named up front, rather than left to fail later as a fetch against a URL with
            // "{{channelId}}" still in the middle of it.
            var fields = DescribeFields(template);
            var labels = missing.Select(key =>
                fields.FirstOrDefault(f => f.Key == key)?.Label ?? key);

            throw new ValidationException("variables", $"Still needed: {string.Join(", ", labels)}.");
        }

        return (template.Type, TemplateRenderer.Render(baseConfig, values), template.SuggestedSchedule);
    }

    public async Task SeedBuiltinsAsync(CancellationToken ct = default)
    {
        var existing = await db.SourceTemplates
            .Where(t => t.Builtin && t.Key != null)
            .ToDictionaryAsync(t => t.Key!, ct);

        foreach (var builtin in BuiltinSourceTemplates.All)
        {
            if (existing.TryGetValue(builtin.Key!, out var current))
            {
                // Updated in place so a corrected feed path reaches sources already using it,
                // rather than leaving everyone on the version they happened to create against.
                current.Name = builtin.Name;
                current.Description = builtin.Description;
                current.Type = builtin.Type;
                current.BaseConfig = builtin.BaseConfig;
                current.Fields = builtin.Fields;
                current.SuggestedSchedule = builtin.SuggestedSchedule;
            }
            else
            {
                db.SourceTemplates.Add(builtin);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static SourceTemplateResponse Describe(SourceTemplate template) => new(
        template.Id,
        template.Key,
        template.Name,
        template.Description,
        template.Type,
        template.SuggestedSchedule,
        template.Builtin,
        DescribeFields(template).Select(f =>
            new SourceTemplateFieldResponse(f.Key, f.Label, f.Placeholder, f.Help, f.Required)).ToList());

    private static IReadOnlyList<TemplateField> DescribeFields(SourceTemplate template) =>
        JsonSerializer.Deserialize<List<TemplateField>>(template.Fields) ?? [];
}
