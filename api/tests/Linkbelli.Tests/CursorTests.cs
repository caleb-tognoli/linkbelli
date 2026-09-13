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

    // --- Keyset positions ---

    [Fact]
    public void TimeKey_RoundTrips()
    {
        var at = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(89);
        var id = Guid.NewGuid();

        var key = Cursor.DecodeTimeKey(Cursor.EncodeTimeKey(at, id));

        // To the tick. Rounding to the second would put every row created in the same second on
        // the wrong side of the page break.
        Assert.Equal(at, key!.Value.At);
        Assert.Equal(id, key.Value.Id);
    }

    [Fact]
    public void DecodeTimeKey_TreatsNoCursorAsAFirstPage()
    {
        Assert.Null(Cursor.DecodeTimeKey(null));
        Assert.Null(Cursor.DecodeTimeKey(""));
    }

    /// <summary>
    /// A cursor that was sent and cannot be read is a 400, not a silent restart. Handing back
    /// page one pretends a broken client is working, which is how a paging bug survives a year.
    /// </summary>
    [Theory]
    // Not base64 at all.
    [InlineData("not base64!!")]
    // Base64 of something that is not a position.
    [InlineData("bm9uc2Vuc2U=")]
    // The right shape with the halves the wrong way round.
    [InlineData("YmFuYW5hOjE3MDAwMDAwMDA=")]
    public void DecodeTimeKey_RejectsWhatItCannotRead(string cursor)
    {
        Assert.Throws<ValidationException>(() => Cursor.DecodeTimeKey(cursor));
    }

    /// <summary>
    /// An empty cursor is a client saying nothing, not a client saying something wrong —
    /// <c>?cursor=</c> comes off a form as readily as it comes off a bug.
    /// </summary>
    [Fact]
    public void DecodeTimeKey_TreatsAnEmptyCursorAsNoCursor()
    {
        Assert.Null(Cursor.DecodeTimeKey(Cursor.Encode("")));
    }

    [Fact]
    public void DecodeTimeKey_RejectsTheOffsetCursorThisReplaced()
    {
        // What every cursor used to be: base64 of a row count. Accepting it would mean silently
        // restarting a scroll at the top after a deploy.
        Assert.Throws<ValidationException>(() => Cursor.DecodeTimeKey(Cursor.Encode("150")));
    }

    [Fact]
    public void DecodeTimeKey_RejectsATimestampOutsideTheCalendar()
    {
        // Forged rather than mistyped. Worth a 400 and not a 500.
        Assert.Throws<ValidationException>(
            () => Cursor.DecodeTimeKey(Cursor.Encode($"{long.MaxValue}:{Guid.NewGuid()}")));
    }

    [Fact]
    public void DecodeOffset_AcceptsACountAndRefusesTheRest()
    {
        Assert.Equal(0, Cursor.DecodeOffset(null));
        Assert.Equal(150, Cursor.DecodeOffset(Cursor.Encode("150")));

        Assert.Throws<ValidationException>(() => Cursor.DecodeOffset(Cursor.Encode("-1")));
        Assert.Throws<ValidationException>(() => Cursor.DecodeOffset(Cursor.Encode("banana")));
    }

    [Fact]
    public void DecodePage_SeparatesAbsentFromUnreadable()
    {
        Assert.False(Cursor.DecodePage(null, out _, out _));
        Assert.True(Cursor.DecodePage(Cursor.EncodePage(3, "1"), out var total, out _));
        Assert.Equal(3, total);

        Assert.Throws<ValidationException>(() => Cursor.DecodePage("not base64!!", out _, out _));
        // A cursor with no total prefix: minted before that existed, or invented.
        Assert.Throws<ValidationException>(() => Cursor.DecodePage(Cursor.Encode("2048"), out _, out _));
    }
}
