using System.Text;

namespace Linkbelli.Application.Common;

/// <summary>Encodes/decodes the opaque pagination cursor. Clients must treat it as a black box.</summary>
public static class Cursor
{
    /// <summary>Separates the carried total from the sort-specific payload in a page cursor.</summary>
    private const char TotalSeparator = '|';

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
}
