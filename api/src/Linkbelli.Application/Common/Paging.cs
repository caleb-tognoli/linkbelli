namespace Linkbelli.Application.Common;

/// <summary>
/// How many rows a listing hands back.
/// </summary>
/// <remarks>
/// Out-of-range limits used to be clamped: <c>limit=0</c>, <c>limit=-5</c> and <c>limit=99999</c>
/// all returned 200 with a silently corrected page size. A client asking for ninety-nine thousand
/// rows has a bug, and quietly giving it a hundred hides that bug until somebody wonders why
/// their sync client only ever sees the first page. Absent is still a default — that is a client
/// declining to choose, not a client getting it wrong.
/// </remarks>
public static class Paging
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    /// <summary>The page size to use, or a 400 naming the range that was allowed.</summary>
    public static int Take(int? limit, int max = MaxLimit, int fallback = DefaultLimit)
    {
        if (limit is null)
        {
            return fallback;
        }

        if (limit < 1 || limit > max)
        {
            throw new ValidationException("limit", $"limit must be between 1 and {max}.");
        }

        return limit.Value;
    }
}
