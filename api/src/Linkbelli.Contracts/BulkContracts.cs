using Linkbelli.Core.Entities;

namespace Linkbelli.Contracts;

/// <summary>What to do to a selection of items.</summary>
public enum BulkAction
{
    /// <summary>Soft delete — recoverable from the trash like any other delete.</summary>
    Delete,

    /// <summary>Set the status (watched / unwatched).</summary>
    SetStatus,

    /// <summary>Set or clear the score.</summary>
    SetScore,

    /// <summary>Move into another playlist the caller owns.</summary>
    Move,

    /// <summary>Copy into another playlist, leaving the originals in place.</summary>
    Copy,
}

/// <summary>
/// One action applied to a selection. Every one of these exists per item already; what was
/// missing is doing it to forty of them without forty round trips.
/// </summary>
public record BulkItemRequest(
    Guid[] ItemIds,
    BulkAction Action,
    /// <summary>Required for <see cref="BulkAction.SetStatus"/>.</summary>
    PlaylistItemStatus? Status = null,
    /// <summary>For <see cref="BulkAction.SetScore"/>. Null clears the score.</summary>
    int? Score = null,
    /// <summary>Required for <see cref="BulkAction.Move"/> and <see cref="BulkAction.Copy"/>.</summary>
    Guid? TargetPlaylistId = null);

/// <summary>
/// What actually happened. <c>Skipped</c> covers items the caller doesn't own and, for a copy or
/// move, links already present in the target — neither is an error worth failing the batch over.
/// </summary>
public record BulkItemResult(int Affected, int Skipped);
