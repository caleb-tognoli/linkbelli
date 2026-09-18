namespace Linkbelli.Core.Entities;

/// <summary>
/// The readable text of a link's page, kept at enrichment.
/// </summary>
/// <remarks>
/// Without it a saved article is only searchable by the handful of words in its title, and
/// cannot be read back once the original rots.
///
/// Its own entity so that nothing loads it by accident. It used to be a property on
/// <see cref="Link"/>, and every place that materialised a link — the automation sweep reading
/// two hundred items a minute, every save, every enrichment, every restore — dragged the whole
/// article along with it. Measured on this app's own data: reading two hundred links took 19 ms
/// with the text and half a millisecond without, before any of it crossed the wire.
///
/// The same row, not another table. Postgres already keeps text this size out of line, so the
/// row itself was never the problem — what was loaded was — and the search vector is generated
/// from this column on this row, which a separate table would have broken. Loaded explicitly,
/// by the reader and by highlighting, and by nothing else.
/// </remarks>
public class LinkContent
{
    /// <summary>The link's own id: this is the same row, seen from the side.</summary>
    public Guid Id { get; set; }

    /// <summary>Paragraphs separated by a blank line. Null when the page had no article in it, which most pages do not.</summary>
    public string? Text { get; set; }
}
