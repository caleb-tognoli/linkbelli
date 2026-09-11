namespace Linkbelli.Core.Content;

/// <summary>
/// What a saved link actually is. A collection is a single undifferentiated list without it, and
/// "what can I watch now" and "what can I read on the train" are different questions.
/// </summary>
public enum ContentKind
{
    /// <summary>Not yet classified, or nothing about it said what it was.</summary>
    Unknown = 0,

    /// <summary>Prose worth reading — the reading time on the row belongs to these.</summary>
    Article = 1,

    Video = 2,

    /// <summary>A code repository.</summary>
    Repository = 3,

    /// <summary>A paper: arXiv, a DOI, a journal.</summary>
    Paper = 4,

    /// <summary>A PDF or another document served as a file rather than a page.</summary>
    Document = 5,

    Audio = 6,

    Image = 7,

    /// <summary>A post on a social platform, which is neither an article nor a video.</summary>
    Social = 8,
}
