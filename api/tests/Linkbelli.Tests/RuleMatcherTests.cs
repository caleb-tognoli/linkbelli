using Linkbelli.Core.Automation;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

public class RuleMatcherTests
{
    private static readonly Guid Inbox = Guid.NewGuid();

    private static RuleCandidate Candidate(
        string url = "https://example.com/post",
        string host = "example.com",
        string? title = "A post about Rust",
        ContentKind kind = ContentKind.Article,
        Guid? playlistId = null) =>
        new(playlistId ?? Inbox, url, host, title, kind);

    private static CompiledRule Rule(Action<AutomationRule> configure)
    {
        var rule = new AutomationRule { Name = "Test", OwnerId = Guid.NewGuid() };
        configure(rule);
        return new CompiledRule(rule);
    }

    [Fact]
    public void A_rule_with_no_conditions_matches_everything()
    {
        // "Tag everything that arrives" is a real thing to want, and it is what was written.
        Assert.True(Rule(_ => { }).Matches(Candidate()));
    }

    [Fact]
    public void A_disabled_rule_matches_nothing()
    {
        Assert.False(Rule(r => r.Enabled = false).Matches(Candidate()));
    }

    [Fact]
    public void Conditions_are_all_required_rather_than_any()
    {
        // Or-ing them makes a two-condition rule nearly impossible to predict.
        var rule = Rule(r =>
        {
            r.Host = "example.com";
            r.TitlePattern = "rust";
        });

        Assert.True(rule.Matches(Candidate()));
        Assert.False(rule.Matches(Candidate(title: "A post about Go")));
        Assert.False(rule.Matches(Candidate(host: "other.com")));
    }

    [Fact]
    public void The_host_must_match_exactly()
    {
        // Not a suffix: "example.com" shouldn't quietly capture "notexample.com".
        var rule = Rule(r => r.Host = "example.com");

        Assert.True(rule.Matches(Candidate(host: "EXAMPLE.COM")));
        Assert.False(rule.Matches(Candidate(host: "notexample.com")));
        Assert.False(rule.Matches(Candidate(host: "sub.example.com")));
    }

    [Fact]
    public void A_rule_can_be_scoped_to_one_playlist()
    {
        var elsewhere = Guid.NewGuid();
        var rule = Rule(r => r.PlaylistId = Inbox);

        Assert.True(rule.Matches(Candidate()));
        Assert.False(rule.Matches(Candidate(playlistId: elsewhere)));
    }

    [Fact]
    public void A_kind_condition_narrows_to_that_kind()
    {
        var rule = Rule(r => r.Kind = ContentKind.Video);

        Assert.True(rule.Matches(Candidate(kind: ContentKind.Video)));
        Assert.False(rule.Matches(Candidate(kind: ContentKind.Article)));
    }

    [Fact]
    public void An_untitled_item_cannot_satisfy_a_title_pattern()
    {
        var rule = Rule(r => r.TitlePattern = "rust");

        Assert.False(rule.Matches(Candidate(title: null)));
        Assert.False(rule.Matches(Candidate(title: "")));
    }

    [Fact]
    public void Url_patterns_see_the_whole_address()
    {
        var rule = Rule(r => r.UrlPattern = "/tag/");

        Assert.True(rule.Matches(Candidate(url: "https://example.com/tag/rust")));
        Assert.False(rule.Matches(Candidate(url: "https://example.com/posts/rust")));
    }

    [Fact]
    public void A_pattern_that_will_not_parse_is_rejected_at_compile_time()
    {
        Assert.ThrowsAny<ArgumentException>(() => CompiledRule.Build("("));
    }

    [Fact]
    public void An_over_long_pattern_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => CompiledRule.Build(new string('a', CompiledRule.MaxPatternLength + 1)));
    }

    [Fact]
    public async Task A_pattern_built_to_backtrack_forever_does_not_hang()
    {
        var rule = Rule(r => r.TitlePattern = "(a+)+$");
        var candidate = Candidate(title: new string('a', 40) + "!");

        var matched = await Task.Run(() => rule.Matches(candidate)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(matched);
    }
}
