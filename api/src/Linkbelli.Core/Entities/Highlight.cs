namespace Linkbelli.Core.Entities;

/// <summary>
/// A passage somebody marked in an article they saved.
/// </summary>
/// <remarks>
/// The app already did the expensive, differentiating half of what a read-later tool does: it
/// extracts the readable article and keeps it, on the reasoning that the copy on the web is the
/// part that rots. Then it rendered that text as inert paragraphs. Highlighting is the most-used
/// feature of every tool in this category, and it is the thing that makes the stored copy worth
/// more than the link. One free-text note per item was not a substitute — a note cannot point at
/// anything.
///
/// Held against the <em>link</em> and its owner rather than against a playlist item, which is
/// where the obvious design would put it. A link in two playlists is one article: the reader
/// already unions reading progress across copies for exactly this reason, and a highlight that
/// existed in one list and not the other would be the same passage marked twice with no way to
/// tell which was meant.
/// </remarks>
public class Highlight : BaseEntity<Guid>
{
    /// <summary>The longest passage that will be stored, in characters.</summary>
    /// <remarks>
    /// A selection is a sentence or a paragraph, not a chapter; and somebody dragging over the
    /// whole article has selected everything, which is not a highlight. Long enough that no
    /// ordinary selection hits it, short enough that the table cannot become a second copy of
    /// the articles.
    /// </remarks>
    public const int MaxTextLength = 2_000;

    public const int MaxNoteLength = 1_000;

    public Guid OwnerId { get; set; }

    public Guid LinkId { get; set; }

    /// <summary>Which paragraph of the stored article this is in, counting from zero.</summary>
    public int ParagraphIndex { get; set; }

    /// <summary>Character offset of the first character, within that paragraph.</summary>
    public int Start { get; set; }

    /// <summary>Character offset one past the last character, within that paragraph.</summary>
    public int End { get; set; }

    /// <summary>
    /// The words themselves, stored alongside the offsets rather than derived from them.
    /// </summary>
    /// <remarks>
    /// An article can be enriched again — a paywall lifts, an extractor improves — and the
    /// paragraphs shift underneath. Keeping the quote means that when the offsets no longer line
    /// up, the highlight degrades to something a person can still read, instead of silently
    /// pointing at the wrong words.
    /// </remarks>
    public string Text { get; set; } = string.Empty;

    /// <summary>What the reader had to say about it. Null when the passage was note enough.</summary>
    public string? Note { get; set; }

    public Link? Link { get; set; }
}
