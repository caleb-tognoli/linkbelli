using Linkbelli.Core.Automation;
using Linkbelli.Core.Content;
using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

/// <summary>
/// The conditions a rule gained.
/// </summary>
/// <remarks>
/// Search could ask "articles under five minutes" and "anything broken" from the day it shipped;
/// rules could not, off exactly the same stored columns. The Rules page's own example copy
/// suggested "send anything over twenty minutes to a later list" — a condition the engine could
/// not express.
/// </remarks>
public class RuleConditionTests
{
    private static readonly Guid Playlist = Guid.NewGuid();

    private static RuleCandidate Item(
        int? words = null, bool broken = false, Guid? sourceId = null, ContentKind kind = ContentKind.Article) =>
        new(Playlist, "https://example.com/a", "example.com", "A title", kind, words, broken, sourceId);

    private static CompiledRule Rule(Action<AutomationRule> configure)
    {
        var rule = new AutomationRule { Name = "Test" };
        configure(rule);
        return new CompiledRule(rule);
    }

    /// <summary>The Rules page's own example, now expressible.</summary>
    [Fact]
    public void Anything_over_twenty_minutes()
    {
        var rule = Rule(r => r.MinMinutes = 20);

        // 220 words a minute, so 6,000 words is about 27 minutes and 2,000 is about 9.
        Assert.True(rule.Matches(Item(words: 6_000)));
        Assert.False(rule.Matches(Item(words: 2_000)));
    }

    [Fact]
    public void Anything_under_five_minutes()
    {
        var rule = Rule(r => r.MaxMinutes = 5);

        Assert.True(rule.Matches(Item(words: 600)));
        Assert.False(rule.Matches(Item(words: 6_000)));
    }

    [Fact]
    public void A_range_needs_both_ends_to_hold()
    {
        var rule = Rule(r =>
        {
            r.MinMinutes = 5;
            r.MaxMinutes = 15;
        });

        Assert.True(rule.Matches(Item(words: 2_000)));
        Assert.False(rule.Matches(Item(words: 300)));
        Assert.False(rule.Matches(Item(words: 8_000)));
    }

    /// <summary>
    /// "Under five minutes" is a claim about a piece of writing. A video is not a short read —
    /// it is not a read at all, and letting it through would file every video as a quick one.
    /// </summary>
    [Fact]
    public void Something_with_no_article_behind_it_has_no_length_to_compare()
    {
        Assert.False(Rule(r => r.MaxMinutes = 5).Matches(Item(words: null, kind: ContentKind.Video)));
        Assert.False(Rule(r => r.MinMinutes = 5).Matches(Item(words: null, kind: ContentKind.Video)));
    }

    /// <summary>A rule with no length condition does not care whether there is an article.</summary>
    [Fact]
    public void Without_a_length_condition_it_does_not_come_up()
    {
        Assert.True(Rule(r => r.Kind = ContentKind.Video).Matches(Item(words: null, kind: ContentKind.Video)));
    }

    [Fact]
    public void Broken_can_be_asked_for_either_way_or_not_at_all()
    {
        Assert.True(Rule(r => r.Broken = true).Matches(Item(broken: true)));
        Assert.False(Rule(r => r.Broken = true).Matches(Item(broken: false)));

        Assert.True(Rule(r => r.Broken = false).Matches(Item(broken: false)));
        Assert.False(Rule(r => r.Broken = false).Matches(Item(broken: true)));

        // Null is "either", not "false".
        Assert.True(Rule(r => r.Broken = null).Matches(Item(broken: true)));
    }

    [Fact]
    public void A_source_narrows_to_what_it_brought_in()
    {
        var source = Guid.NewGuid();
        var rule = Rule(r => r.SourceId = source);

        Assert.True(rule.Matches(Item(sourceId: source)));
        Assert.False(rule.Matches(Item(sourceId: Guid.NewGuid())));

        // A link somebody added by hand has no source, so a source condition excludes it.
        Assert.False(rule.Matches(Item(sourceId: null)));
    }

    /// <summary>
    /// Conditions are AND, because a rule that fires when any of its conditions matches is
    /// nearly impossible to predict once it has more than one.
    /// </summary>
    [Fact]
    public void Every_condition_has_to_hold()
    {
        var source = Guid.NewGuid();
        var rule = Rule(r =>
        {
            r.MaxMinutes = 10;
            r.SourceId = source;
            r.Broken = false;
        });

        Assert.True(rule.Matches(Item(words: 1_000, sourceId: source)));
        Assert.False(rule.Matches(Item(words: 1_000, sourceId: Guid.NewGuid())));
        Assert.False(rule.Matches(Item(words: 9_000, sourceId: source)));
        Assert.False(rule.Matches(Item(words: 1_000, sourceId: source, broken: true)));
    }

    /// <summary>
    /// The estimate rounds, so the boundary has to agree with the "5 min" printed on the row —
    /// a filter that disagrees with the label beside it is worse than no filter at all.
    /// </summary>
    [Fact]
    public void The_boundary_agrees_with_the_reading_time_shown()
    {
        var words = 5 * ReadingTime.WordsPerMinute;
        Assert.Equal(5, ReadingTime.Minutes(words));

        Assert.True(Rule(r => r.MaxMinutes = 5).Matches(Item(words: words)));
        Assert.True(Rule(r => r.MinMinutes = 5).Matches(Item(words: words)));
    }
}
