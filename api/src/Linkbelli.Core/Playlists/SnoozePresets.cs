namespace Linkbelli.Core.Playlists;

/// <summary>
/// When "not now" means.
/// </summary>
/// <remarks>
/// Presets rather than a date picker. "Not now" is a feeling, not a date — being made to choose a
/// Tuesday to express it is why snooze buttons go unused, and the five below cover what people
/// actually mean when they put something aside.
///
/// Pure and local-time aware: the whole point of "tonight" is that it is tonight where the person
/// is, and a UTC evening is somebody else's lunchtime.
/// </remarks>
public static class SnoozePresets
{
    /// <summary>The evening, when reading actually happens.</summary>
    public const int EveningHour = 19;

    /// <summary>The morning, for something that belongs to a working day.</summary>
    public const int MorningHour = 8;

    /// <summary>The names accepted, in the order a UI should offer them.</summary>
    public static readonly IReadOnlyList<string> Names = ["tonight", "tomorrow", "weekend", "week", "month"];

    /// <summary>
    /// Resolves a preset against the caller's own clock, or null if it is not one of ours.
    /// </summary>
    /// <param name="preset">One of <see cref="Names"/>, case-insensitively.</param>
    /// <param name="now">The moment to count from, in the reader's local time.</param>
    public static DateTimeOffset? Resolve(string? preset, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(preset))
        {
            return null;
        }

        return preset.Trim().ToLowerInvariant() switch
        {
            // Tonight, unless it already is tonight — in which case the person means tomorrow
            // evening, not four minutes from now.
            "tonight" => now.Hour < EveningHour
                ? At(now, EveningHour)
                : At(now.AddDays(1), EveningHour),

            "tomorrow" => At(now.AddDays(1), MorningHour),

            // The next Saturday morning. On a Saturday or Sunday it means the one coming, not
            // the one you are in: somebody snoozing on Saturday is not asking for five minutes.
            "weekend" => At(NextSaturday(now), MorningHour),

            "week" => At(now.AddDays(7), MorningHour),
            "month" => At(now.AddMonths(1), MorningHour),
            _ => null,
        };
    }

    private static DateTimeOffset At(DateTimeOffset day, int hour) =>
        new(day.Year, day.Month, day.Day, hour, 0, 0, day.Offset);

    private static DateTimeOffset NextSaturday(DateTimeOffset now)
    {
        var days = ((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7;

        // Already the weekend: the next one, a week out.
        return now.AddDays(days == 0 ? 7 : days);
    }
}
