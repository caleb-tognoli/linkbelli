using Cronos;

namespace Linkbelli.Application.Sources;

/// <summary>Validates 5-field cron expressions, including a minimum interval between runs.</summary>
public static class CronSchedule
{
    /// <summary>
    /// True if <paramref name="cron"/> is a valid 5-field expression whose consecutive runs
    /// are never closer together than <paramref name="minIntervalMinutes"/>.
    /// </summary>
    public static bool IsValid(string? cron, int minIntervalMinutes)
    {
        var trimmed = cron?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !CronExpression.TryParse(trimmed, out var expression))
        {
            return false;
        }

        // Sample upcoming occurrences and ensure no two are closer than the minimum interval.
        var from = DateTime.UtcNow;
        var occurrences = expression
            .GetOccurrences(from, from.AddHours(6), fromInclusive: false, toInclusive: true)
            .Take(200)
            .ToList();

        for (var i = 1; i < occurrences.Count; i++)
        {
            if ((occurrences[i] - occurrences[i - 1]).TotalMinutes < minIntervalMinutes)
            {
                return false;
            }
        }

        return true;
    }
}
