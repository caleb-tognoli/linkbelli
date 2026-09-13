using Linkbelli.Core.Playlists;

namespace Linkbelli.Tests;

/// <summary>
/// When "not now" means.
/// </summary>
/// <remarks>
/// Presets rather than a date picker: "not now" is a feeling, not a date, and being made to
/// choose a Tuesday to express it is why snooze buttons go unused. Which means the five have to
/// land where somebody would expect, or they are worse than the picker.
/// </remarks>
public class SnoozePresetTests
{
    private static DateTimeOffset At(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tonight_is_this_evening_when_it_is_still_the_day()
    {
        // A Wednesday lunchtime.
        var resolved = SnoozePresets.Resolve("tonight", At(2026, 3, 4, 12));

        Assert.Equal(At(2026, 3, 4, SnoozePresets.EveningHour), resolved);
    }

    /// <summary>
    /// Somebody putting something aside at ten at night means tomorrow evening, not four minutes
    /// from now — which is the bug this preset would otherwise have.
    /// </summary>
    [Fact]
    public void Tonight_after_the_evening_means_tomorrow_evening()
    {
        var resolved = SnoozePresets.Resolve("tonight", At(2026, 3, 4, 22));

        Assert.Equal(At(2026, 3, 5, SnoozePresets.EveningHour), resolved);
    }

    [Fact]
    public void Tomorrow_is_the_next_morning()
    {
        var resolved = SnoozePresets.Resolve("tomorrow", At(2026, 3, 4, 22));

        Assert.Equal(At(2026, 3, 5, SnoozePresets.MorningHour), resolved);
    }

    [Fact]
    public void The_weekend_is_the_next_saturday_morning()
    {
        // 2026-03-04 is a Wednesday.
        var wednesday = At(2026, 3, 4, 12);
        Assert.Equal(DayOfWeek.Wednesday, wednesday.DayOfWeek);

        var resolved = SnoozePresets.Resolve("weekend", wednesday);

        Assert.Equal(At(2026, 3, 7, SnoozePresets.MorningHour), resolved);
        Assert.Equal(DayOfWeek.Saturday, resolved!.Value.DayOfWeek);
    }

    /// <summary>
    /// Snoozing on a Saturday is not asking for five minutes. It means the weekend after this
    /// one, which is the only reading of "the weekend" that is any use on a Saturday.
    /// </summary>
    [Fact]
    public void The_weekend_on_a_saturday_is_the_one_after()
    {
        var saturday = At(2026, 3, 7, 10);
        Assert.Equal(DayOfWeek.Saturday, saturday.DayOfWeek);

        var resolved = SnoozePresets.Resolve("weekend", saturday);

        Assert.Equal(At(2026, 3, 14, SnoozePresets.MorningHour), resolved);
    }

    [Fact]
    public void A_week_and_a_month_are_what_they_say()
    {
        var now = At(2026, 3, 4, 12);

        Assert.Equal(At(2026, 3, 11, SnoozePresets.MorningHour), SnoozePresets.Resolve("week", now));
        Assert.Equal(At(2026, 4, 4, SnoozePresets.MorningHour), SnoozePresets.Resolve("month", now));
    }

    [Fact]
    public void Every_preset_lands_in_the_future()
    {
        var now = At(2026, 3, 4, 23);

        foreach (var name in SnoozePresets.Names)
        {
            var resolved = SnoozePresets.Resolve(name, now);

            Assert.NotNull(resolved);
            Assert.True(resolved > now, $"{name} resolved to {resolved}, which is not later than {now}.");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("next tuesday")]
    public void Anything_else_is_not_one_of_ours(string? preset)
    {
        Assert.Null(SnoozePresets.Resolve(preset, At(2026, 3, 4, 12)));
    }

    /// <summary>The offset is the caller's. A UTC evening is somebody else's lunchtime.</summary>
    [Fact]
    public void A_preset_keeps_the_callers_offset()
    {
        var tokyo = new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.FromHours(9));

        var resolved = SnoozePresets.Resolve("tonight", tokyo);

        Assert.Equal(TimeSpan.FromHours(9), resolved!.Value.Offset);
        Assert.Equal(SnoozePresets.EveningHour, resolved.Value.Hour);
    }
}
