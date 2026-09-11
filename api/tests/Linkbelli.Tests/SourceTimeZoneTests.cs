using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

public class SourceTimeZoneTests
{
    [Fact]
    public void An_absent_zone_means_utc()
    {
        Assert.Equal(TimeZoneInfo.Utc, SourceTimeZone.Resolve(null));
        Assert.Equal(TimeZoneInfo.Utc, SourceTimeZone.Resolve(""));
        Assert.Equal(TimeZoneInfo.Utc, SourceTimeZone.Resolve("   "));
    }

    [Theory]
    [InlineData("Europe/Rome")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    [InlineData("UTC")]
    public void Iana_zones_resolve(string id)
    {
        var resolved = SourceTimeZone.Resolve(id);

        Assert.NotNull(resolved);
        Assert.True(SourceTimeZone.IsValid(id));
    }

    [Fact]
    public void A_resolved_zone_actually_offsets_from_utc()
    {
        // Mid-summer in Rome is UTC+2; the point is that the zone does something, not just parses.
        var rome = SourceTimeZone.Resolve("Europe/Rome");
        var midsummer = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromHours(2), rome.GetUtcOffset(midsummer));
    }

    [Fact]
    public void A_zone_observes_daylight_saving()
    {
        var rome = SourceTimeZone.Resolve("Europe/Rome");

        var winter = rome.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var summer = rome.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));

        // This drift is exactly what a UTC-only schedule could not express.
        Assert.NotEqual(winter, summer);
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("not a zone")]
    public void An_unknown_zone_is_rejected_by_validation(string id)
    {
        Assert.False(SourceTimeZone.IsValid(id));
    }

    [Fact]
    public void An_unknown_zone_still_resolves_to_utc_rather_than_throwing()
    {
        // Validation rejects these on the way in. If one is already stored — a host that dropped
        // a zone, say — the source should keep running at the wrong hour rather than stop dead.
        Assert.Equal(TimeZoneInfo.Utc, SourceTimeZone.Resolve("Mars/Olympus_Mons"));
    }
}
