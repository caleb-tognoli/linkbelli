using Linkbelli.Core.Content;

namespace Linkbelli.Contracts;

/// <summary>
/// One "when this arrives, do that" over your own collection. Conditions are all required: a rule
/// that fires when any one of several conditions matches is nearly impossible to predict.
/// </summary>
public record AutomationRuleResponse(
    Guid Id,
    string Name,
    bool Enabled,
    /// <summary>Rules run lowest first; order matters because one can move an item out from under the next.</summary>
    int Position,
    // --- When ---
    /// <summary>Only items landing in this playlist; null applies it everywhere.</summary>
    Guid? PlaylistId,
    string? Host,
    string? TitlePattern,
    string? UrlPattern,
    ContentKind? Kind,
    // --- Then ---
    string[] AddTags,
    Guid? MoveToPlaylistId,
    Guid? CopyToPlaylistId,
    bool MarkWatched,
    bool Trash,
    /// <summary>Stop after this rule matches, so a specific rule can shield an item from a broad one.</summary>
    bool StopOnMatch,
    /// <summary>How many items it has acted on — what makes a rule that never fires visible.</summary>
    int MatchCount,
    DateTimeOffset? LastMatchedAt,
    DateTimeOffset CreationTime);

public record CreateAutomationRuleRequest(
    string Name,
    Guid? PlaylistId = null,
    string? Host = null,
    string? TitlePattern = null,
    string? UrlPattern = null,
    ContentKind? Kind = null,
    string[]? AddTags = null,
    Guid? MoveToPlaylistId = null,
    Guid? CopyToPlaylistId = null,
    bool MarkWatched = false,
    bool Trash = false,
    bool StopOnMatch = false,
    bool Enabled = true,
    int? Position = null);

/// <summary>Every field is optional; omitted ones are left as they are.</summary>
public record UpdateAutomationRuleRequest(
    string? Name = null,
    bool? Enabled = null,
    int? Position = null,
    Guid? PlaylistId = null,
    string? Host = null,
    string? TitlePattern = null,
    string? UrlPattern = null,
    ContentKind? Kind = null,
    string[]? AddTags = null,
    Guid? MoveToPlaylistId = null,
    Guid? CopyToPlaylistId = null,
    bool? MarkWatched = null,
    bool? Trash = null,
    bool? StopOnMatch = null,
    /// <summary>
    /// Clears the conditions or destinations named here. Needed because null already means
    /// "leave it alone", so there is otherwise no way to say "stop filtering by host".
    /// </summary>
    string[]? Clear = null);

/// <summary>What a rule would do, tried against what is already saved rather than what arrives next.</summary>
public record AutomationPreviewResponse(int Matches, IReadOnlyList<AutomationPreviewItem> Sample);

public record AutomationPreviewItem(Guid ItemId, string PlaylistName, string Url, string? Title);
