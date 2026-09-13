using Linkbelli.Contracts;

namespace Linkbelli.Application.Common;

/// <summary>
/// A row plus the values it was ordered by.
/// </summary>
/// <remarks>
/// Keyset paging needs the last row's sort key to build the next cursor, and the responses
/// themselves have no field for it — "when this playlist was last touched" is computed for the
/// ordering and never shown. Rather than widen every response with a column nobody reads, the key
/// rides alongside as far as the service boundary and is dropped there.
/// </remarks>
public record KeyedRow<T>(DateTimeOffset At, Guid Id, T Row);

public static class KeyedRowExtensions
{
    /// <summary>
    /// Trims the extra row that was fetched to detect a next page, and names where to resume.
    /// </summary>
    /// <remarks>
    /// One row more than asked for is fetched so that "is there more" needs no second query; if
    /// it came back, the page is full and the cursor points at its last row.
    /// </remarks>
    public static PagedResult<T> ToPage<T>(this List<KeyedRow<T>> rows, int take)
    {
        string? next = null;
        if (rows.Count > take)
        {
            rows.RemoveAt(take);
            next = Cursor.EncodeTimeKey(rows[^1].At, rows[^1].Id);
        }

        return new PagedResult<T>([.. rows.Select(r => r.Row)], next);
    }
}
