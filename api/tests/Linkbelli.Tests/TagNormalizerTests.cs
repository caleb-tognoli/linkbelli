using Linkbelli.Core.Tags;

namespace Linkbelli.Tests;

public class TagNormalizerTests
{
    [Theory]
    [InlineData("  Hello World  ", "hello world")]
    [InlineData("Tech", "tech")]
    [InlineData("a   b\tc", "a b c")] // collapse internal whitespace
    [InlineData("   ", "")]
    public void NormalizeOne_trims_lowercases_collapses(string input, string expected)
    {
        Assert.Equal(expected, TagNormalizer.NormalizeOne(input));
    }

    [Fact]
    public void Normalize_dedupes_case_insensitively_and_drops_empties()
    {
        var result = TagNormalizer.Normalize(["Tech", "tech", "  ", "AI", "ai "]);
        Assert.Equal(["tech", "ai"], result);
    }

    [Fact]
    public void Normalize_caps_count_and_length()
    {
        var many = Enumerable.Range(0, 50).Select(i => $"tag{i}");
        Assert.Equal(TagNormalizer.MaxPerPlaylist, TagNormalizer.Normalize(many).Count);

        var huge = new string('x', 200);
        Assert.Equal(TagNormalizer.MaxLength, TagNormalizer.Normalize([huge])[0].Length);
    }

    [Fact]
    public void Normalize_null_is_empty()
    {
        Assert.Empty(TagNormalizer.Normalize(null));
    }
}
