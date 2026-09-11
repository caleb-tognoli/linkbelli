namespace Linkbelli.Contracts;

public record QuotaResponse(
    int MaxSources, int SourcesUsed,
    int MaxRunsPerDay, int RunsUsedToday,
    int MaxItemsPerRun);

/// <summary>Admin request to set a user's quota limits.</summary>
public record SetQuotaRequest(int MaxSources, int MaxRunsPerDay, int MaxItemsPerRun);

/// <summary>
/// What someone actually has here. Quotas were invisible until a 429 landed, and nothing at all
/// reported the size of a collection — so there was no way to see it growing.
/// </summary>
public record UsageResponse(
    int Playlists,
    int Items,
    /// <summary>Links still being fetched, so an item count that looks short explains itself.</summary>
    int PendingItems,
    int Folders,
    int Sources,
    int SavedSearches,
    int Sites,
    /// <summary>Items marked watched — progress, rather than just volume.</summary>
    int Watched,
    /// <summary>Links whose page is gone or unreadable.</summary>
    int Broken,
    /// <summary>Playlists and items in the trash, still restorable.</summary>
    int InTrash);
