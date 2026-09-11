using Linkbelli.Core.Sources;

namespace Linkbelli.Tests;

public class SourceFilterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    private static bool Accepts(
        SourceFilter filter, string url = "https://example.com/a", string? title = "A post", DateTimeOffset? published = null) =>
        filter.Compile().Accepts(url, title, published, Now);

    [Fact]
    public void An_empty_filter_accepts_everything()
    {
        var filter = new SourceFilter();

        Assert.True(filter.IsEmpty);
        Assert.True(Accepts(filter, title: null, published: Now));
    }

    [Fact]
    public void A_filter_with_any_field_set_is_not_empty()
    {
        Assert.False(new SourceFilter { MaxItems = 5 }.IsEmpty);
        Assert.False(new SourceFilter { TitleExclude = "spam" }.IsEmpty);
    }

    [Theory]
    [InlineData("Rust 1.90 released", true)]
    [InlineData("Go 1.26 released", false)]
    public void Title_include_keeps_only_matches(string title, bool expected)
    {
        Assert.Equal(expected, Accepts(new SourceFilter { TitleInclude = "rust" }, title: title));
    }

    [Fact]
    public void Title_exclude_wins_over_include()
    {
        var filter = new SourceFilter { TitleInclude = "rust", TitleExclude = "sponsored" };

        Assert.True(Accepts(filter, title: "Rust at work"));
        Assert.False(Accepts(filter, title: "Rust at work [sponsored]"));
    }

    [Fact]
    public void An_untitled_link_cannot_satisfy_a_title_include()
    {
        // Filtering on titles means "titles like this", not "anything the feed forgot to name".
        Assert.False(Accepts(new SourceFilter { TitleInclude = "rust" }, title: null));
        Assert.False(Accepts(new SourceFilter { TitleInclude = "rust" }, title: ""));
    }

    [Fact]
    public void An_untitled_link_is_not_excluded_by_a_title_exclude()
    {
        Assert.True(Accepts(new SourceFilter { TitleExclude = "spam" }, title: null));
    }

    [Fact]
    public void Patterns_are_case_insensitive()
    {
        Assert.True(Accepts(new SourceFilter { TitleInclude = "RUST" }, title: "rust"));
        Assert.False(Accepts(new SourceFilter { UrlExclude = "/TAG/" }, url: "https://example.com/tag/x"));
    }

    [Fact]
    public void Url_patterns_see_the_whole_address()
    {
        var filter = new SourceFilter { UrlInclude = @"^https://blog\.example\.com/" };

        Assert.True(Accepts(filter, url: "https://blog.example.com/post"));
        Assert.False(Accepts(filter, url: "https://shop.example.com/post"));
    }

    [Fact]
    public void Minimum_age_holds_back_what_was_just_published()
    {
        var filter = new SourceFilter { MinAgeHours = 24 };

        Assert.False(Accepts(filter, published: Now.AddHours(-1)));
        Assert.True(Accepts(filter, published: Now.AddHours(-25)));
    }

    [Fact]
    public void Minimum_age_does_not_apply_to_links_with_no_date()
    {
        // "Unknown" is not "new": a scraper reports no dates at all, and treating that as
        // brand-new would filter out its entire output forever.
        Assert.True(Accepts(new SourceFilter { MinAgeHours = 24 }, published: null));
    }

    [Fact]
    public void Lookarounds_still_work_even_though_the_fast_engine_lacks_them()
    {
        var filter = new SourceFilter { TitleInclude = "^(?!re:).*release" };

        Assert.True(Accepts(filter, title: "Big release"));
        Assert.False(Accepts(filter, title: "re: big release"));
    }

    [Fact]
    public void A_pattern_that_will_not_parse_is_rejected_at_compile_time()
    {
        // Thrown where a save can catch it, so a run never meets a pattern it can't use.
        Assert.ThrowsAny<ArgumentException>(() => CompiledSourceFilter.Build("("));
    }

    [Fact]
    public void An_over_long_pattern_is_rejected()
    {
        var pattern = new string('a', SourceFilter.MaxPatternLength + 1);

        Assert.Throws<ArgumentException>(() => CompiledSourceFilter.Build(pattern));
    }

    [Fact]
    public void Blank_patterns_are_no_pattern_at_all()
    {
        Assert.Null(CompiledSourceFilter.Build(null));
        Assert.Null(CompiledSourceFilter.Build("   "));
    }

    [Fact]
    public async Task A_pattern_built_to_backtrack_forever_does_not_hang()
    {
        // The classic: nested quantifiers over a non-matching tail. The linear engine handles it
        // outright; anything it can't take runs behind a timeout instead.
        var filter = new SourceFilter { TitleInclude = "(a+)+$" };
        var title = new string('a', 40) + "!";

        var accepted = await Task.Run(() => Accepts(filter, title: title))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(accepted);
    }
}
