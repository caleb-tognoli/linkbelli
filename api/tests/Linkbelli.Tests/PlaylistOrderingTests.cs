using Linkbelli.Core.Playlists;

namespace Linkbelli.Tests;

public class PlaylistOrderingTests
{
    [Fact]
    public void First_item_gets_one_gap()
    {
        Assert.Equal(PlaylistOrdering.Gap, PlaylistOrdering.Between(null, null));
    }

    [Fact]
    public void Appending_adds_a_gap_past_the_last()
    {
        Assert.Equal(5000 + PlaylistOrdering.Gap, PlaylistOrdering.Between(5000, null));
    }

    [Fact]
    public void Inserting_between_returns_the_midpoint()
    {
        Assert.Equal(1500, PlaylistOrdering.Between(1000, 2000));
    }

    [Fact]
    public void Inserting_at_front_halves_toward_zero()
    {
        Assert.Equal(512, PlaylistOrdering.Between(null, 1024));
    }

    [Theory]
    [InlineData(1000L, 1001L)] // adjacent
    [InlineData(1000L, 1000L)] // equal
    [InlineData(null, 1L)]     // no room before the first
    public void Returns_null_when_no_room_to_signal_renumber(long? before, long? after)
    {
        Assert.Null(PlaylistOrdering.Between(before, after));
    }
}
