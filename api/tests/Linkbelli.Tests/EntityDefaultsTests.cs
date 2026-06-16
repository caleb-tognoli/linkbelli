using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

public class EntityDefaultsTests
{
    [Fact]
    public void PlaylistItem_position_gap_leaves_room_for_reordering()
    {
        Assert.True(PlaylistItem.PositionGap >= 2);
    }

    [Fact]
    public void New_playlist_defaults_to_private()
    {
        var playlist = new Playlist { Name = "test", Slug = "test" };
        Assert.Equal(PlaylistVisibility.Private, playlist.Visibility);
    }

    [Fact]
    public void New_source_has_a_schedule()
    {
        var source = new Source { Name = "test", Config = "{}", Schedule = "0 * * * *" };
        Assert.Equal("0 * * * *", source.Schedule);
    }
}
