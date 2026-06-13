using System.Text;

namespace Linkbelli.Application.Common;

/// <summary>Encodes/decodes the opaque pagination cursor. Clients must treat it as a black box.</summary>
public static class Cursor
{
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
}
