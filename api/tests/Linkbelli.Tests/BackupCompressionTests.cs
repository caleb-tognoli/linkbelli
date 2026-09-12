using System.Text;
using Linkbelli.Application.Backups;

namespace Linkbelli.Tests;

/// <summary>
/// A backup is only worth the bytes it stores if those bytes come back unchanged. These cover the
/// round-trip, including the parts of a real export most likely to survive badly.
/// </summary>
public class BackupCompressionTests
{
    [Fact]
    public void What_goes_in_comes_back_out()
    {
        const string json = """{"username":"alice","playlists":[{"name":"Reading"}]}""";

        Assert.Equal(json, BackupService.Decompress(BackupService.Compress(json)));
    }

    [Fact]
    public void Titles_that_are_not_english_survive()
    {
        // Exports carry whatever titles the web gave them; a round-trip that mangles these is a
        // backup that quietly corrupts the thing it exists to protect.
        const string json = """{"title":"日本語 — naïve café «quotes» 🎧","note":"line\nbreak\ttab"}""";

        Assert.Equal(json, BackupService.Decompress(BackupService.Compress(json)));
    }

    [Fact]
    public void An_empty_library_round_trips()
    {
        const string json = """{"playlists":[]}""";

        Assert.Equal(json, BackupService.Decompress(BackupService.Compress(json)));
    }

    [Fact]
    public void Repetitive_json_compresses_to_a_fraction_of_itself()
    {
        // An export is the same twenty keys repeated once per link. If that did not compress,
        // storing five snapshots per account would not be a reasonable thing to do.
        var items = string.Join(",", Enumerable.Range(0, 500).Select(i =>
            $$"""{"url":"https://example.com/{{i}}","title":"Item {{i}}","status":"Unread","score":null}"""));
        var json = $$"""{"playlists":[{"name":"Big","items":[{{items}}]}]}""";

        var compressed = BackupService.Compress(json);

        Assert.True(
            compressed.Length * 5 < Encoding.UTF8.GetByteCount(json),
            $"Expected better than 5:1, got {Encoding.UTF8.GetByteCount(json)} -> {compressed.Length}.");
    }
}
