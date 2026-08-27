namespace Linkbelli.Contracts;

/// <summary>A page of results with an opaque cursor for the next page (null when exhausted).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    /// <summary>Total matching rows across all pages, when the endpoint chooses to compute it.</summary>
    public int? Total { get; init; }
}
