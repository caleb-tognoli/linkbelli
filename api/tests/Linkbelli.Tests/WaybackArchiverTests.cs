using System.Text.Json;
using Linkbelli.Application.Archiving;

namespace Linkbelli.Tests;

public class WaybackArchiverTests
{
    private static string? Snapshot(string json) =>
        WaybackArchiver.ReadSnapshot(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void A_snapshot_is_read_out_of_the_nest_it_arrives_in()
    {
        var json = """
            {"url":"example.com","archived_snapshots":{"closest":{
              "status":"200","available":true,
              "url":"http://web.archive.org/web/20260101000000/https://example.com/",
              "timestamp":"20260101000000"}}}
            """;

        Assert.Equal("https://web.archive.org/web/20260101000000/https://example.com/", Snapshot(json));
    }

    [Fact]
    public void No_snapshot_is_reported_as_an_empty_object_not_an_absent_one()
    {
        // What the availability API actually returns for a page it has never seen.
        Assert.Null(Snapshot("""{"url":"example.com","archived_snapshots":{}}"""));
    }

    [Fact]
    public void A_snapshot_marked_unavailable_is_not_one()
    {
        var json = """
            {"archived_snapshots":{"closest":{"available":false,
              "url":"http://web.archive.org/web/1/https://example.com/"}}}
            """;

        Assert.Null(Snapshot(json));
    }

    [Fact]
    public void A_response_missing_the_field_entirely_does_not_throw()
    {
        Assert.Null(Snapshot("""{"url":"example.com"}"""));
        Assert.Null(Snapshot("{}"));
    }

    [Theory]
    // These end up in an href on a page served over https, and both forms come back in practice.
    [InlineData("//web.archive.org/web/1/x", "https://web.archive.org/web/1/x")]
    [InlineData("http://web.archive.org/web/1/x", "https://web.archive.org/web/1/x")]
    [InlineData("https://web.archive.org/web/1/x", "https://web.archive.org/web/1/x")]
    public void Snapshot_addresses_are_normalized_to_https(string given, string expected)
    {
        Assert.Equal(expected, WaybackArchiver.Https(given));
    }

    [Fact]
    public void The_archived_page_keeps_its_own_scheme_inside_the_snapshot_url()
    {
        // Only the leading scheme is rewritten — the original address is part of the path.
        Assert.Equal(
            "https://web.archive.org/web/1/http://example.com/",
            WaybackArchiver.Https("http://web.archive.org/web/1/http://example.com/"));
    }
}
