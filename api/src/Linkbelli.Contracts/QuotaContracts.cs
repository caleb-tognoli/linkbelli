namespace Linkbelli.Contracts;

public record QuotaResponse(
    int MaxSources, int SourcesUsed,
    int MaxRunsPerDay, int RunsUsedToday,
    int MaxItemsPerRun);

/// <summary>Admin request to set a user's quota limits.</summary>
public record SetQuotaRequest(int MaxSources, int MaxRunsPerDay, int MaxItemsPerRun);
