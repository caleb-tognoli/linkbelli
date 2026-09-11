using System.Text.RegularExpressions;

namespace Linkbelli.Core.Sources;

/// <summary>
/// What a source is allowed to bring in. Everything a source finds lands unconditionally today,
/// which makes a broad feed an all-or-nothing choice: subscribe to the firehose or don't
/// subscribe at all.
/// </summary>
/// <remarks>
/// Stored as jsonb on the source. Every field is optional; an all-null filter is the old
/// behaviour and is stored as null rather than as an object full of nulls.
/// </remarks>
public sealed record SourceFilter
{
    /// <summary>Only accept links whose title matches. Null accepts any title.</summary>
    public string? TitleInclude { get; init; }

    /// <summary>Reject links whose title matches. Applied after <see cref="TitleInclude"/>.</summary>
    public string? TitleExclude { get; init; }

    public string? UrlInclude { get; init; }

    public string? UrlExclude { get; init; }

    /// <summary>
    /// Skip anything published more recently than this. Only applies when the source reports a
    /// publish date; a link with no date is accepted, because "unknown" is not "new".
    /// </summary>
    public int? MinAgeHours { get; init; }

    /// <summary>
    /// Cap on links accepted per run, applied after the patterns so the cap keeps the items you
    /// asked for rather than the first N the feed happened to list.
    /// </summary>
    public int? MaxItems { get; init; }

    /// <summary>
    /// Don't re-add a link this source already put in the playlist and that was removed within
    /// this many days. Without it, deleting something a source found is temporary: the next run
    /// puts it straight back, and there is no way to say no permanently.
    /// </summary>
    public int? DedupeWindowDays { get; init; }

    /// <summary>Longest pattern accepted. Long patterns are where the pathological ones live.</summary>
    public const int MaxPatternLength = 200;

    /// <summary>Ceiling on <see cref="MinAgeHours"/> — 30 days, past which a feed has moved on.</summary>
    public const int MaxMinAgeHours = 720;

    /// <summary>
    /// Ceiling on <see cref="DedupeWindowDays"/>. This is the trash retention: the window is
    /// answered from the removed items themselves, and once those are purged there is nothing
    /// left to remember the removal by.
    /// </summary>
    public const int MaxDedupeWindowDays = 30;

    /// <summary>Whether this filter would change anything. An empty one is stored as null.</summary>
    public bool IsEmpty =>
        TitleInclude is null && TitleExclude is null && UrlInclude is null && UrlExclude is null
        && MinAgeHours is null && MaxItems is null && DedupeWindowDays is null;

    /// <summary>Compiles the patterns once, for a whole run's worth of links.</summary>
    public CompiledSourceFilter Compile() => new(this);
}

/// <summary>
/// A <see cref="SourceFilter"/> with its patterns compiled. Built once per run: compiling four
/// regexes per link would be most of the cost of a run that finds a hundred of them.
/// </summary>
public sealed class CompiledSourceFilter
{
    private readonly Regex? _titleInclude;
    private readonly Regex? _titleExclude;
    private readonly Regex? _urlInclude;
    private readonly Regex? _urlExclude;

    public SourceFilter Filter { get; }

    public CompiledSourceFilter(SourceFilter filter)
    {
        Filter = filter;
        _titleInclude = Build(filter.TitleInclude);
        _titleExclude = Build(filter.TitleExclude);
        _urlInclude = Build(filter.UrlInclude);
        _urlExclude = Build(filter.UrlExclude);
    }

    /// <summary>
    /// Compiles one user-supplied pattern, or returns null for none. Throws
    /// <see cref="ArgumentException"/> on a pattern the engine can't parse — callers validate at
    /// save time so a run never has to deal with it.
    /// </summary>
    public static Regex? Build(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        if (pattern.Length > SourceFilter.MaxPatternLength)
        {
            throw new ArgumentException($"Pattern is longer than {SourceFilter.MaxPatternLength} characters.", nameof(pattern));
        }

        const RegexOptions common = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        try
        {
            // Linear-time matching, so a pattern written by the person it runs for can't hang the
            // worker on an unlucky title.
            return new Regex(pattern, common | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            // Lookarounds and backreferences, which the linear engine doesn't implement. Fall
            // back to the backtracking one behind a timeout — slower to fail, but bounded.
            return new Regex(pattern, common, TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    /// Whether one discovered link gets through. <paramref name="published"/> is what the source
    /// reported, if it reported anything.
    /// </summary>
    public bool Accepts(string url, string? title, DateTimeOffset? published, DateTimeOffset now)
    {
        // An empty title can't match an include pattern, and an owner filtering on titles means
        // "titles like this", not "anything untitled".
        if (_titleInclude is not null && !Matches(_titleInclude, title))
        {
            return false;
        }

        if (_titleExclude is not null && Matches(_titleExclude, title))
        {
            return false;
        }

        if (_urlInclude is not null && !Matches(_urlInclude, url))
        {
            return false;
        }

        if (_urlExclude is not null && Matches(_urlExclude, url))
        {
            return false;
        }

        if (Filter.MinAgeHours is { } hours && published is { } date && date > now.AddHours(-hours))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// A pattern that times out is treated as "didn't match" rather than failing the run. One
    /// pathological title shouldn't cost the owner every other link in the feed.
    /// </summary>
    private static bool Matches(Regex regex, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
