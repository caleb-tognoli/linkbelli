using System.Linq.Expressions;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>
/// A source that is running cleanly and bringing back nothing.
/// </summary>
/// <remarks>
/// The loud failure was always reported: a source that errors repeatedly stops itself and says
/// so, once. This is the quiet one. A scraper whose <c>linkSelector</c> stopped matching after a
/// site redesign, or a feed that now returns an empty document, succeeds every single time and
/// adds nothing — so no status could ever reveal it, and the playlist just stops filling.
/// <c>SkippedCount</c> exists precisely because "a strict filter and a broken selector look
/// identical from the outside"; the data to tell them apart was being recorded and never shown.
///
/// One definition, used by both the weekly digest and the badge on the sources page, because two
/// definitions that drift are worse than one that is slightly wrong.
/// </remarks>
public static class QuietSource
{
    /// <summary>How far back to look. The same window the digest covers.</summary>
    public const int WindowDays = 7;

    /// <summary>
    /// Whether a source counts as quiet, given the moment the window starts.
    /// </summary>
    /// <remarks>
    /// It has to have actually run: a source that has not run at all is either new, stopped or
    /// scheduled monthly, and none of those is the thing being looked for. And it has to be
    /// unmuted, because some sources are meant to be quiet and saying so weekly is noise.
    /// </remarks>
    public static Expression<Func<Source, bool>> Since(
        DateTimeOffset since, IQueryable<SourceRun> runs) =>
        s => s.Status == SourceStatus.Active
            && !s.MuteQuietAlerts
            && runs.Any(r => r.SourceId == s.Id && r.CreationTime >= since)
            && !runs.Any(r => r.SourceId == s.Id && r.CreationTime >= since && r.AddedCount > 0);
}
