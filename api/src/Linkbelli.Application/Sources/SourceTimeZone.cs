namespace Linkbelli.Application.Sources;

/// <summary>
/// Resolves the IANA zone a source's cron is read in. Schedules were previously interpreted in
/// UTC with no way to say otherwise, so "every day at 8" meant 08:00 UTC for everyone and
/// drifted by an hour twice a year for most of the world.
/// </summary>
public static class SourceTimeZone
{
    /// <summary>
    /// The zone for a stored value, or UTC when it is absent. Falls back to UTC rather than
    /// throwing if the host doesn't recognise the id — a schedule running at the wrong hour beats
    /// a source that silently stops running at all.
    /// </summary>
    public static TimeZoneInfo Resolve(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            return TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
    }

    /// <summary>Whether a value is a zone this host can actually schedule against.</summary>
    public static bool IsValid(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone) || TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);
}
