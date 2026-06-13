using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

public class CronScheduleTests
{
    [Theory]
    [InlineData("*/5 * * * *")]   // exactly every 5 minutes
    [InlineData("*/15 * * * *")]  // every 15 minutes
    [InlineData("0 * * * *")]     // hourly
    [InlineData("0 0 * * *")]     // daily
    public void Accepts_schedules_at_least_five_minutes_apart(string cron)
    {
        Assert.True(CronSchedule.IsValid(cron, 5));
    }

    [Theory]
    [InlineData("* * * * *")]     // every minute
    [InlineData("*/2 * * * *")]   // every 2 minutes
    [InlineData("0,1 * * * *")]   // 1-minute gap at the top of each hour
    public void Rejects_schedules_more_frequent_than_five_minutes(string cron)
    {
        Assert.False(CronSchedule.IsValid(cron, 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("99 * * * *")]
    public void Rejects_invalid_expressions(string cron)
    {
        Assert.False(CronSchedule.IsValid(cron, 5));
    }
}
