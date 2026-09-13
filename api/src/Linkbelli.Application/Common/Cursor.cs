using System.Globalization;
using System.Text;

namespace Linkbelli.Application.Common;

/// <summary>Where a page ended, for a listing ordered by a timestamp and then an id.</summary>
/// <remarks>
/// The id is not decoration. A source run inserts all of its items in one SaveChanges, so they
/// share a creation timestamp to the tick; a cursor comparing timestamps alone skips every tied
/// row after the page break.
/// </remarks>
public readonly record struct TimeKey(DateTimeOffset At, Guid Id);

/// <summary>Encodes/decodes the opaque pagination cursor. Clients must treat it as a black box.</summary>
public static class Cursor
{
    /// <summary>Separates the carried total from the sort-specific payload in a page cursor.</summary>
    private const char TotalSeparator = '|';

    /// <summary>Separates the two halves of a keyset position.</summary>
    private const char KeySeparator = ':';

    public static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    public static bool TryDecode(string? cursor, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        try
        {
            value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes a page cursor that carries the result total alongside the sort-specific payload,
    /// so subsequent pages don't have to re-run COUNT(*) over the whole filtered set.
    /// </summary>
    public static string EncodePage(int total, string payload) =>
        Encode($"{total}{TotalSeparator}{payload}");

    /// <summary>
    /// Decodes a cursor produced by <see cref="EncodePage"/>. Returns false for a null/absent or
    /// malformed cursor (the caller then treats the request as a first page and counts).
    /// </summary>
    public static bool TryDecodePage(string? cursor, out int total, out string payload)
    {
        total = 0;
        payload = string.Empty;

        if (!TryDecode(cursor, out var raw))
        {
            return false;
        }

        var separator = raw.IndexOf(TotalSeparator);
        if (separator < 0 || !int.TryParse(raw[..separator], out total))
        {
            return false;
        }

        payload = raw[(separator + 1)..];
        return true;
    }

    /// <summary>
    /// Reads a cursor made by <see cref="EncodePage"/>. False when there is no cursor at all —
    /// a first page, which the caller answers by counting. Throws when one was sent and could
    /// not be read.
    /// </summary>
    public static bool DecodePage(string? cursor, out int total, out string payload)
    {
        total = 0;
        payload = string.Empty;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        if (!TryDecodePage(cursor, out total, out payload))
        {
            throw Malformed();
        }

        return true;
    }

    /// <summary>An offset carried inside a page cursor, rejecting one that is not a number.</summary>
    public static int ParseOffset(string payload)
    {
        if (payload.Length == 0)
        {
            return 0;
        }

        return int.TryParse(payload, CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : throw Malformed();
    }

    // --- Keyset positions ---
    //
    // A cursor that is really an offset pages by counting rows, so it shifts under the reader
    // whenever anything is inserted or removed between requests — on an infinite-scroll feed that
    // shows up as links appearing twice or not at all. These name the last row of the page
    // instead, which is stable no matter what happens around it.

    /// <summary>The payload naming a row, for pairing with a total in <see cref="EncodePage"/>.</summary>
    public static string FormatTimeKey(DateTimeOffset at, Guid id) =>
        $"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}{KeySeparator}{id}";

    /// <summary>A whole cursor naming a row.</summary>
    public static string EncodeTimeKey(DateTimeOffset at, Guid id) => Encode(FormatTimeKey(at, id));

    /// <summary>Reads a payload written by <see cref="FormatTimeKey"/>, or null if it isn't one.</summary>
    public static TimeKey? ParseTimeKey(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        var separator = payload.IndexOf(KeySeparator);
        if (separator <= 0
            || !long.TryParse(payload[..separator], CultureInfo.InvariantCulture, out var ticks)
            || !Guid.TryParse(payload[(separator + 1)..], out var id))
        {
            return null;
        }

        // Outside the representable range this throws rather than returning false, and a forged
        // cursor is not worth a 500.
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return null;
        }

        return new TimeKey(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    /// <summary>
    /// Where to resume, or null for a first page.
    /// </summary>
    /// <remarks>
    /// A cursor that is present but unreadable is a 400, not a silent restart. It can only come
    /// from a client that mangled one or invented one, and handing back page one pretends a
    /// broken client is working — which is how a paging bug stays invisible for a year.
    /// </remarks>
    public static TimeKey? DecodeTimeKey(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        return TryDecode(cursor, out var raw) ? ParseTimeKey(raw) ?? throw Malformed() : throw Malformed();
    }

    /// <summary>Where to resume for a listing that still pages by offset, or 0 for a first page.</summary>
    public static int DecodeOffset(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return 0;
        }

        if (!TryDecode(cursor, out var raw) || !int.TryParse(raw, CultureInfo.InvariantCulture, out var offset)
            || offset < 0)
        {
            throw Malformed();
        }

        return offset;
    }

    /// <summary>
    /// The 400 for a cursor that was sent and could not be read.
    /// </summary>
    /// <remarks>
    /// Public because the sort-specific payloads are parsed by whoever wrote them — a shuffle
    /// seed, a position, a relevance offset — and they should all refuse in the same words.
    /// </remarks>
    public static ValidationException Malformed() =>
        new("cursor", "That cursor is not one of ours. Ask for the first page without it.");
}
