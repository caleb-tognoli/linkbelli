using Linkbelli.Application.Common;

namespace Linkbelli.Tests;

public class CursorTests
{
    [Fact]
    public void Encode_RoundTrips()
    {
        var encoded = Cursor.Encode("1024");
        Assert.True(Cursor.TryDecode(encoded, out var value));
        Assert.Equal("1024", value);
    }

    [Fact]
    public void TryDecode_RejectsNullAndGarbage()
    {
        Assert.False(Cursor.TryDecode(null, out _));
        Assert.False(Cursor.TryDecode("", out _));
        Assert.False(Cursor.TryDecode("not base64!!", out _));
    }

    [Fact]
    public void EncodePage_CarriesTotalAndPayload()
    {
        var encoded = Cursor.EncodePage(417, "2048");

        Assert.True(Cursor.TryDecodePage(encoded, out var total, out var payload));
        Assert.Equal(417, total);
        Assert.Equal("2048", payload);
    }

    [Fact]
    public void EncodePage_PayloadMayContainSeparatorsOfItsOwn()
    {
        // The shuffle cursor's payload is "seed:offset" — the total is split off at the first
        // '|' only, so anything after it survives verbatim.
        var encoded = Cursor.EncodePage(9, "-0.4218:150");

        Assert.True(Cursor.TryDecodePage(encoded, out var total, out var payload));
        Assert.Equal(9, total);
        Assert.Equal("-0.4218:150", payload);
    }

    [Fact]
    public void TryDecodePage_ReturnsFalseForAbsentOrLegacyCursor()
    {
        // No cursor at all: the caller treats it as a first page and counts.
        Assert.False(Cursor.TryDecodePage(null, out _, out _));

        // A cursor without the total prefix (e.g. one minted before this change) also falls
        // back to counting rather than mis-parsing a payload as a total.
        Assert.False(Cursor.TryDecodePage(Cursor.Encode("2048"), out _, out _));
    }

    [Fact]
    public void TryDecodePage_ZeroTotalIsCarried()
    {
        var encoded = Cursor.EncodePage(0, "0");

        Assert.True(Cursor.TryDecodePage(encoded, out var total, out var payload));
        Assert.Equal(0, total);
        Assert.Equal("0", payload);
    }
}
