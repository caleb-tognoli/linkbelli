using Linkbelli.Application.Common;

namespace Linkbelli.Tests;

/// <summary>
/// How many rows a listing hands back.
/// </summary>
/// <remarks>
/// These used to be clamped: limit=0, limit=-5 and limit=99999 all returned 200 with a silently
/// corrected page size. A client asking for ninety-nine thousand rows has a bug, and quietly
/// giving it a hundred hides that bug until somebody wonders why their sync client only ever
/// sees the first page.
/// </remarks>
public class PagingTests
{
    [Fact]
    public void No_limit_is_a_client_declining_to_choose_not_a_client_getting_it_wrong()
    {
        Assert.Equal(Paging.DefaultLimit, Paging.Take(null));
        Assert.Equal(25, Paging.Take(null, fallback: 25));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(Paging.MaxLimit)]
    public void A_limit_inside_the_range_is_used_as_asked(int limit)
    {
        Assert.Equal(limit, Paging.Take(limit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    [InlineData(99999)]
    public void A_limit_outside_the_range_is_refused_rather_than_corrected(int limit)
    {
        Assert.Throws<ValidationException>(() => Paging.Take(limit));
    }

    [Fact]
    public void The_refusal_names_the_range_it_would_have_accepted()
    {
        var error = Assert.Throws<ValidationException>(() => Paging.Take(500, max: 24));

        // "limit must be between 1 and 24" — a client cannot fix what it is not told.
        Assert.Contains("24", Assert.Single(error.Errors["limit"]));
    }
}
