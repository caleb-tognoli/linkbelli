using Linkbelli.Application.Enrichment;

namespace Linkbelli.Tests;

public class ArticleExtractorTests
{
    private readonly ArticleExtractor _extractor = new();

    /// <summary>A paragraph long enough that a few of them clear the "is this an article" bar.</summary>
    private static string Paragraph(int words = 30) =>
        string.Join(' ', Enumerable.Range(0, words).Select(n => $"word{n}"));

    [Fact]
    public void The_article_is_read_and_the_furniture_is_not()
    {
        var html = $"""
            <html><body>
              <nav><a href="/">Home</a><a href="/about">About</a></nav>
              <header>Site name</header>
              <article>
                <h1>The headline</h1>
                <p>{Paragraph()}</p>
                <p>{Paragraph()}</p>
                <script>console.log('tracking')</script>
              </article>
              <aside><p>{Paragraph()} sidebar</p></aside>
              <footer>Copyright</footer>
            </body></html>
            """;

        var article = _extractor.Extract(html);

        Assert.NotNull(article.Text);
        Assert.StartsWith("The headline", article.Text);
        Assert.DoesNotContain("tracking", article.Text);
        Assert.DoesNotContain("sidebar", article.Text);
        Assert.DoesNotContain("Copyright", article.Text);
    }

    [Fact]
    public void The_biggest_block_of_prose_wins_when_nothing_declares_itself()
    {
        // No <article>, no <main> — the shape of most CMS output.
        var html = $"""
            <html><body>
              <div class="rail"><p>Related: a short teaser.</p></div>
              <div class="story"><p>{Paragraph()}</p><p>{Paragraph()}</p><p>{Paragraph()}</p></div>
            </body></html>
            """;

        var article = _extractor.Extract(html);

        Assert.NotNull(article.Text);
        Assert.DoesNotContain("teaser", article.Text);
    }

    [Fact]
    public void Paragraphs_are_separated_and_whitespace_is_collapsed()
    {
        var html = $"""
            <article>
              <p>First    paragraph
                 over   two lines.</p>
              <p>{Paragraph()}</p>
              <p>{Paragraph()}</p>
            </article>
            """;

        var article = _extractor.Extract(html);

        Assert.StartsWith("First paragraph over two lines.\n\n", article.Text);
    }

    [Fact]
    public void A_list_item_is_read_once_not_once_per_nested_paragraph()
    {
        var html = $"""
            <article>
              <p>{Paragraph()}</p>
              <p>{Paragraph()}</p>
              <ul><li><p>Only once</p></li></ul>
            </article>
            """;

        var article = _extractor.Extract(html);

        Assert.Equal(1, CountOccurrences(article.Text!, "Only once"));
    }

    [Fact]
    public void A_page_with_barely_any_prose_has_no_article_in_it()
    {
        // A caption or a cookie notice. Offering a reader view for one sentence is worse than
        // offering none.
        var article = _extractor.Extract("<article><p>Accept cookies to continue.</p></article>");

        Assert.Null(article.Text);
        Assert.False(article.Truncated);
    }

    [Fact]
    public void Words_are_counted_before_anything_is_cut()
    {
        var html = $"<article>{string.Concat(Enumerable.Repeat($"<p>{Paragraph(200)}</p>", 60))}</article>";

        var article = _extractor.Extract(html);

        Assert.True(article.Truncated);
        Assert.True(article.Text!.Length <= ArticleExtractor.MaxLength);
        // 12,000 words: the count describes the article, not the part of it that was kept.
        Assert.Equal(12_000, article.WordCount);
    }

    [Fact]
    public void Truncation_stops_at_a_word_boundary()
    {
        var html = $"<article>{string.Concat(Enumerable.Repeat($"<p>{Paragraph(200)}</p>", 60))}</article>";

        var article = _extractor.Extract(html);

        Assert.DoesNotContain("  ", article.Text);
        Assert.False(article.Text!.EndsWith(' '));
    }

    [Fact]
    public void An_empty_page_yields_nothing_rather_than_throwing()
    {
        var article = _extractor.Extract("");

        Assert.Null(article.Text);
        Assert.Equal(0, article.WordCount);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
