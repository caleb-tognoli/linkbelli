using System.Text.Json;
using Linkbelli.Application.Common;
using Linkbelli.Core.Sources;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Validates a source filter at save time and moves it in and out of the source's jsonb column.
/// A pattern is checked where the person who typed it can be told about it, so a run never has
/// to decide what to do with one that won't compile.
/// </summary>
public static class SourceFilters
{
    /// <summary>
    /// Checks and tidies a filter from a request. Returns null for one that would change nothing,
    /// so "no filter" is stored as null rather than as an object full of nulls.
    /// </summary>
    /// <exception cref="ValidationException">A pattern won't compile, or a number is out of range.</exception>
    public static SourceFilter? Normalize(SourceFilter? filter)
    {
        if (filter is null)
        {
            return null;
        }

        var normalized = new SourceFilter
        {
            TitleInclude = Pattern(filter.TitleInclude, "filter.titleInclude"),
            TitleExclude = Pattern(filter.TitleExclude, "filter.titleExclude"),
            UrlInclude = Pattern(filter.UrlInclude, "filter.urlInclude"),
            UrlExclude = Pattern(filter.UrlExclude, "filter.urlExclude"),
            MinAgeHours = Range(filter.MinAgeHours, 0, SourceFilter.MaxMinAgeHours, "filter.minAgeHours"),
            MaxItems = Range(filter.MaxItems, 1, int.MaxValue, "filter.maxItems"),
            DedupeWindowDays = Range(
                filter.DedupeWindowDays, 0, SourceFilter.MaxDedupeWindowDays, "filter.dedupeWindowDays"),
        };

        return normalized.IsEmpty ? null : normalized;
    }

    public static string? Serialize(SourceFilter? filter) =>
        filter is null ? null : JsonSerializer.Serialize(filter);

    /// <summary>
    /// Reads a stored filter back. A column that won't deserialize is treated as no filter: the
    /// alternative is a source that can never run again, which is a worse answer than one that
    /// brings in too much.
    /// </summary>
    public static SourceFilter? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SourceFilter>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Pattern(string? pattern, string field)
    {
        var trimmed = pattern?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        try
        {
            CompiledSourceFilter.Build(trimmed);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(field, ex.Message);
        }

        return trimmed;
    }

    private static int? Range(int? value, int min, int max, string field)
    {
        if (value is not { } number)
        {
            return null;
        }

        if (number < min || number > max)
        {
            throw new ValidationException(field, $"Must be between {min} and {max}.");
        }

        return number;
    }
}
