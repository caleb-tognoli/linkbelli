using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Linkbelli.Application.Enrichment;

/// <summary>The readable part of a page: its paragraphs, and how much there is of it.</summary>
/// <param name="Text">Paragraphs joined by blank lines, or null when the page has no article in it.</param>
/// <param name="WordCount">Words in <paramref name="Text"/>, before any truncation.</param>
/// <param name="Truncated">Whether the stored text stops short of the whole article.</param>
public record ArticleText(string? Text, int WordCount, bool Truncated);

/// <summary>
/// Pulls the article out of a page and leaves the furniture behind — no I/O, so it is testable
/// from fixtures.
/// </summary>
/// <remarks>
/// Deliberately simple: find the element carrying the most paragraph text, then read its blocks
/// in order. That handles the ordinary case (an <c>article</c> or a content <c>div</c> surrounded
/// by navigation) without pretending to be a full readability implementation.
/// </remarks>
public class ArticleExtractor
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Longest text kept. Longer than all but a handful of articles, and it bounds both the row
    /// and the trigram index that makes the text searchable.
    /// </summary>
    public const int MaxLength = 60_000;

    /// <summary>
    /// Below this, what was found is a caption or a cookie notice rather than something to read.
    /// Stored as no article at all, so a reader view is never offered for one sentence.
    /// </summary>
    public const int MinWords = 60;

    /// <summary>Elements that are never part of what someone came to read.</summary>
    private const string Furniture =
        "script, style, noscript, template, iframe, svg, form, nav, aside, footer, header, " +
        "figure figcaption, .advert, .ad, .share, .social, .newsletter, .cookie, .comments, #comments";

    /// <summary>What separates one stored paragraph from the next.</summary>
    public const string ParagraphSeparator = "\n\n";

    /// <summary>Blocks read, in document order, once the article element is chosen.</summary>
    private const string Blocks = "p, h1, h2, h3, h4, h5, h6, li, blockquote, pre";

    public ArticleText Extract(string html)
    {
        var document = Parser.ParseDocument(html);

        foreach (var element in document.QuerySelectorAll(Furniture).ToList())
        {
            element.Remove();
        }

        var root = FindArticle(document);
        if (root is null)
        {
            return new ArticleText(null, 0, false);
        }

        var paragraphs = new List<string>();
        foreach (var block in root.QuerySelectorAll(Blocks))
        {
            // A <p> inside a <li> would otherwise be read twice, once on its own and once as part
            // of the list item above it.
            if (block.ParentElement?.Closest(Blocks) is not null)
            {
                continue;
            }

            var text = Collapse(block.TextContent);
            if (text.Length > 0)
            {
                paragraphs.Add(text);
            }
        }

        var full = string.Join(ParagraphSeparator, paragraphs);
        var words = CountWords(full);
        if (words < MinWords)
        {
            return new ArticleText(null, words, false);
        }

        return full.Length <= MaxLength
            ? new ArticleText(full, words, false)
            : new ArticleText(Trim(full), words, true);
    }

    /// <summary>
    /// The element the article lives in. A declared one wins outright; otherwise the candidate
    /// carrying the most paragraph text does, which is what separates a story from the column of
    /// links beside it.
    /// </summary>
    private static IElement? FindArticle(IDocument document)
    {
        foreach (var selector in (string[])["article", "[role='main']", "main"])
        {
            var declared = document.QuerySelectorAll(selector)
                .OrderByDescending(ParagraphLength)
                .FirstOrDefault();

            if (declared is not null && ParagraphLength(declared) > 0)
            {
                return declared;
            }
        }

        var best = document.QuerySelectorAll("div, section, td")
            .Select(element => (element, length: ParagraphLength(element)))
            .OrderByDescending(candidate => candidate.length)
            .FirstOrDefault();

        // Nothing with paragraphs in it at all: a link dump, an app shell, or a page whose text
        // arrives by script. Better to store nothing than to store its navigation.
        return best.length > 0 ? best.element : document.Body;
    }

    /// <summary>How much paragraph text an element holds directly beneath it.</summary>
    private static int ParagraphLength(IElement element) =>
        element.QuerySelectorAll("p").Sum(p => p.TextContent.Trim().Length);

    /// <summary>Cuts at a word boundary, so the text doesn't end mid-word.</summary>
    private static string Trim(string text)
    {
        var cut = text.LastIndexOf(' ', MaxLength - 1);
        return text[..(cut > MaxLength / 2 ? cut : MaxLength)];
    }

    /// <summary>Whitespace as one space, which is what HTML means by it anyway.</summary>
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static int CountWords(string text)
    {
        var words = 0;
        var inWord = false;

        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }
}
