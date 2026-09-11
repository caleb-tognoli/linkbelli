using Linkbelli.Core.Sources;

namespace Linkbelli.Tests;

public class TemplateRendererTests
{
    private static Dictionary<string, string> Config(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    [Fact]
    public void Substitutes_a_placeholder()
    {
        var rendered = TemplateRenderer.Render(
            "https://www.youtube.com/feeds/videos.xml?channel_id={{channelId}}",
            new Dictionary<string, string> { ["channelId"] = "UC123" });

        Assert.Equal("https://www.youtube.com/feeds/videos.xml?channel_id=UC123", rendered);
    }

    [Theory]
    [InlineData("{{name}}")]
    [InlineData("{{ name }}")]
    [InlineData("{{  name  }}")]
    public void Tolerates_whitespace_inside_the_braces(string placeholder)
    {
        var rendered = TemplateRenderer.Render(
            placeholder, new Dictionary<string, string> { ["name"] = "value" });

        Assert.Equal("value", rendered);
    }

    [Fact]
    public void Substitutes_the_same_placeholder_everywhere_it_appears()
    {
        var rendered = TemplateRenderer.Render(
            "https://reddit.com/r/{{sub}}/.rss?title={{sub}}",
            new Dictionary<string, string> { ["sub"] = "programming" });

        Assert.Equal("https://reddit.com/r/programming/.rss?title=programming", rendered);
    }

    [Fact]
    public void Trims_a_supplied_value()
    {
        // People paste values with stray whitespace; a URL with a space in it fails obscurely.
        var rendered = TemplateRenderer.Render(
            "https://x.test/{{id}}", new Dictionary<string, string> { ["id"] = "  abc  " });

        Assert.Equal("https://x.test/abc", rendered);
    }

    [Fact]
    public void Leaves_an_unsupplied_placeholder_alone()
    {
        // Left visible so MissingValues can report it, rather than quietly producing
        // "https://x.test/" and failing later as a mysterious fetch error.
        var rendered = TemplateRenderer.Render("https://x.test/{{id}}", new Dictionary<string, string>());

        Assert.Equal("https://x.test/{{id}}", rendered);
    }

    [Fact]
    public void Renders_a_whole_config()
    {
        var template = Config(
            ("url", "https://news.test/{{section}}"),
            ("itemSelector", "li.story"),
            ("meta.title", "a.headline"));

        var rendered = TemplateRenderer.Render(
            template, new Dictionary<string, string> { ["section"] = "world" });

        Assert.Equal("https://news.test/world", rendered["url"]);
        // Everything that isn't a placeholder survives untouched — that is the point of a template.
        Assert.Equal("li.story", rendered["itemSelector"]);
        Assert.Equal("a.headline", rendered["meta.title"]);
    }

    [Fact]
    public void Lists_placeholders_in_the_order_they_first_appear()
    {
        var template = Config(
            ("url", "https://x.test/{{owner}}/{{repo}}"),
            ("urlTemplate", "https://x.test/{{owner}}/{{repo}}/releases/{{id}}"));

        Assert.Equal(["owner", "repo", "id"], TemplateRenderer.Placeholders(template.Values));
    }

    [Fact]
    public void Reports_what_is_missing()
    {
        var template = Config(("url", "https://x.test/{{owner}}/{{repo}}"));

        var missing = TemplateRenderer.MissingValues(
            template, new Dictionary<string, string> { ["owner"] = "linkbelli" });

        Assert.Equal(["repo"], missing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_counts_as_missing(string supplied)
    {
        var template = Config(("url", "https://x.test/{{id}}"));

        Assert.Equal(["id"], TemplateRenderer.MissingValues(
            template, new Dictionary<string, string> { ["id"] = supplied }));
    }

    [Fact]
    public void A_config_with_no_placeholders_needs_nothing()
    {
        var template = Config(("feedUrl", "https://news.test/rss.xml"));

        Assert.Empty(TemplateRenderer.Placeholders(template.Values));
        Assert.Empty(TemplateRenderer.MissingValues(template, new Dictionary<string, string>()));
    }

    [Fact]
    public void Values_are_not_themselves_treated_as_templates()
    {
        // A value containing braces is data, not a second round of substitution to run.
        var rendered = TemplateRenderer.Render(
            "https://x.test/{{id}}", new Dictionary<string, string> { ["id"] = "{{other}}" });

        Assert.Equal("https://x.test/{{other}}", rendered);
    }

    [Theory]
    [InlineData("{{ }}")]
    [InlineData("{{1abc}}")]
    [InlineData("{single}")]
    public void Things_that_are_not_placeholders_are_left_alone(string text)
    {
        Assert.Equal(text, TemplateRenderer.Render(text, new Dictionary<string, string> { ["x"] = "y" }));
    }
}
