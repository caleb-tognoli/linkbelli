using Linkbelli.Core.Search;

namespace Linkbelli.Tests;

/// <summary>
/// Typing the filters that were only ever clickable.
/// </summary>
/// <remarks>
/// Every one of these filters already existed and was exercised on every search — as a chip. So
/// it could be applied but not typed, not shared as a URL somebody else could read, and not saved
/// as a sentence.
///
/// The parser is deliberately narrow. Quoted phrases and <c>-exclusion</c> are not handled here:
/// <c>websearch_to_tsquery</c> already understands both, so the job is to leave them alone.
/// </remarks>
public class SearchOperatorTests
{
    [Fact]
    public void Nothing_typed_parses_to_nothing()
    {
        var parsed = SearchOperators.Parse(null);

        Assert.Null(parsed.Text);
        Assert.Null(parsed.Host);
        Assert.Empty(parsed.ItemTags);
    }

    [Fact]
    public void Plain_words_stay_plain_words()
    {
        var parsed = SearchOperators.Parse("postgres full text search");

        Assert.Equal("postgres full text search", parsed.Text);
        Assert.Null(parsed.Host);
    }

    [Theory]
    [InlineData("site:bbc.co.uk", "bbc.co.uk")]
    [InlineData("host:BBC.CO.UK", "bbc.co.uk")]
    // Pasting a whole address after site: is the obvious mistake to be forgiving about.
    [InlineData("site:https://www.bbc.co.uk/news", "www.bbc.co.uk")]
    [InlineData("site:bbc.co.uk/news", "bbc.co.uk")]
    public void A_site_becomes_a_hostname(string raw, string expected)
    {
        Assert.Equal(expected, SearchOperators.Parse(raw).Host);
    }

    [Fact]
    public void Operators_come_out_and_the_words_stay()
    {
        var parsed = SearchOperators.Parse("site:bbc.co.uk climate under:10 is:unread");

        Assert.Equal("bbc.co.uk", parsed.Host);
        Assert.Equal(10, parsed.MaxMinutes);
        Assert.Equal("unwatched", parsed.Status);
        Assert.Equal("climate", parsed.Text);
    }

    [Fact]
    public void A_tag_may_be_given_more_than_once()
    {
        var parsed = SearchOperators.Parse("tag:rust tag:Async");

        Assert.Equal(["rust", "async"], parsed.ItemTags);
        Assert.Null(parsed.Text);
    }

    [Theory]
    [InlineData("is:read", "watched")]
    [InlineData("is:watched", "watched")]
    [InlineData("is:done", "watched")]
    [InlineData("is:unread", "unwatched")]
    [InlineData("is:unwatched", "unwatched")]
    public void Read_and_unread_have_the_names_people_use(string raw, string expected)
    {
        Assert.Equal(expected, SearchOperators.Parse(raw).Status);
    }

    [Fact]
    public void Broken_is_its_own_thing()
    {
        Assert.True(SearchOperators.Parse("is:broken").Broken);
        Assert.False(SearchOperators.Parse("is:ok").Broken);
        Assert.Null(SearchOperators.Parse("is:unread").Broken);
    }

    [Theory]
    [InlineData("score:>80", 80)]
    [InlineData("score:>=80", 80)]
    [InlineData("score:80", 80)]
    public void A_score_is_a_floor(string raw, int expected)
    {
        Assert.Equal(expected, SearchOperators.Parse(raw).MinScore);
    }

    /// <summary>
    /// The search has no upper bound on score, and quietly treating "below 20" as "at least 20"
    /// would be worse than not accepting it. It stays in the text instead.
    /// </summary>
    [Fact]
    public void A_score_below_something_is_not_pretended_to_work()
    {
        var parsed = SearchOperators.Parse("score:<20");

        Assert.Null(parsed.MinScore);
        Assert.Equal("score:<20", parsed.Text);
    }

    [Theory]
    [InlineData("under:10", 10)]
    [InlineData("under:10m", 10)]
    [InlineData("under:10min", 10)]
    [InlineData("minutes:<10", 10)]
    public void Reading_time_accepts_how_people_write_it(string raw, int expected)
    {
        Assert.Equal(expected, SearchOperators.Parse(raw).MaxMinutes);
    }

    /// <summary>
    /// A colon appears in prose and in addresses far more often than it introduces an operator
    /// nobody defined. An unrecognised one is a search term.
    /// </summary>
    [Theory]
    [InlineData("ratio:1")]
    [InlineData("https://example.com/x")]
    [InlineData("10:30")]
    [InlineData("note:")]
    public void An_unrecognised_operator_stays_in_the_text(string raw)
    {
        var parsed = SearchOperators.Parse(raw);

        Assert.Equal(raw, parsed.Text);
        Assert.Null(parsed.Host);
    }

    /// <summary>
    /// The reason the parser has a tokenizer at all: an operator inside quotes is part of the
    /// phrase, and the phrase goes to Postgres intact.
    /// </summary>
    [Fact]
    public void An_operator_inside_quotes_is_part_of_the_phrase()
    {
        var parsed = SearchOperators.Parse("\"site:reliability\" outage");

        Assert.Null(parsed.Host);
        Assert.Equal("\"site:reliability\" outage", parsed.Text);
    }

    /// <summary>
    /// Both of these are `websearch_to_tsquery`'s to interpret. Reimplementing them here would
    /// mean doing it worse, and in a second place.
    /// </summary>
    [Fact]
    public void Phrases_and_exclusions_are_passed_through_untouched()
    {
        var parsed = SearchOperators.Parse("\"rate limiting\" -kubernetes site:example.com");

        Assert.Equal("example.com", parsed.Host);
        Assert.Equal("\"rate limiting\" -kubernetes", parsed.Text);
    }

    /// <summary>Somebody halfway through typing a quote still gets a search.</summary>
    [Fact]
    public void An_unterminated_quote_takes_the_rest_of_the_line()
    {
        var parsed = SearchOperators.Parse("site:example.com \"half a phrase");

        Assert.Equal("example.com", parsed.Host);
        Assert.Equal("\"half a phrase\"", parsed.Text);
    }

    [Fact]
    public void A_query_of_nothing_but_operators_has_no_text_left()
    {
        var parsed = SearchOperators.Parse("site:example.com is:unread kind:video");

        Assert.Null(parsed.Text);
        Assert.Equal("video", parsed.Kind);
    }
}
