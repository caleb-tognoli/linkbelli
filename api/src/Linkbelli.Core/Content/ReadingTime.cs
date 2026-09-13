namespace Linkbelli.Core.Content;

/// <summary>
/// How long something takes to read.
/// </summary>
/// <remarks>
/// One definition, because three things now depend on it agreeing: the "6 min" printed on a row,
/// the search filter for "under five minutes", and an automation rule that files anything over
/// twenty. A filter that disagrees with the label beside it is worse than no filter at all, and
/// the web app carries the same constant with the same reasoning in
/// <c>web/src/lib/reading.ts</c> — the two are kept in step by hand and by these comments.
/// </remarks>
public static class ReadingTime
{
    /// <summary>
    /// Words a minute for ordinary prose.
    /// </summary>
    /// <remarks>
    /// Adult reading speeds spread from roughly 200 to 300, so this sits deliberately low: a
    /// five-minute estimate that turns out to take four is a pleasant surprise, and one that
    /// takes eight is a broken promise.
    /// </remarks>
    public const int WordsPerMinute = 220;

    /// <summary>
    /// Whole minutes, or null when there is nothing to estimate from.
    /// </summary>
    /// <remarks>
    /// Never zero: "0 min read" reads as an error rather than as "very short". And null rather
    /// than zero for no article, because a video is not a short read — it is not a read at all.
    /// </remarks>
    public static int? Minutes(int? wordCount) =>
        wordCount is null or <= 0 ? null : Math.Max(1, (int)Math.Round(wordCount.Value / (double)WordsPerMinute));

    /// <summary>
    /// The largest word count that still reads in this many minutes.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="Minutes"/>, for a query that has to compare against the stored
    /// column rather than against a computed one. Half a minute of slack, because the estimate
    /// rounds: without it, "under five minutes" excludes a piece the row itself labels "5 min".
    /// </remarks>
    public static int WordsWithin(int minutes) => (minutes * WordsPerMinute) + (WordsPerMinute / 2);
}
