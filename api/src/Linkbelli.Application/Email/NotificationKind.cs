namespace Linkbelli.Application.Email;

/// <summary>
/// The things Linkbelli will email somebody about, unprompted.
/// </summary>
/// <remarks>
/// A short list on purpose. Each of these tells somebody something they could not have found out
/// by looking, which is the whole test for whether it deserves to arrive in an inbox.
/// </remarks>
public enum NotificationKind
{
    /// <summary>Somebody shared a playlist with this user.</summary>
    Share,

    /// <summary>Somebody followed one of this user's playlists.</summary>
    Follow,

    /// <summary>One of this user's sources gave up after repeated failures.</summary>
    SourceStopped,

    /// <summary>The weekly summary.</summary>
    WeeklyDigest,
}

public static class NotificationKinds
{
    /// <summary>The name used in an unsubscribe link and in the preferences API.</summary>
    public static string Slug(this NotificationKind kind) => kind switch
    {
        NotificationKind.Share => "share",
        NotificationKind.Follow => "follow",
        NotificationKind.SourceStopped => "source-stopped",
        _ => "digest",
    };

    /// <summary>Reads a slug back; null when it names nothing.</summary>
    public static NotificationKind? Parse(string? slug) => slug?.Trim().ToLowerInvariant() switch
    {
        "share" => NotificationKind.Share,
        "follow" => NotificationKind.Follow,
        "source-stopped" => NotificationKind.SourceStopped,
        "digest" => NotificationKind.WeeklyDigest,
        _ => null,
    };

    /// <summary>How to describe what somebody is turning off, on the page that confirms it.</summary>
    public static string Describe(this NotificationKind kind) => kind switch
    {
        NotificationKind.Share => "when somebody shares a playlist with you",
        NotificationKind.Follow => "when somebody follows one of your playlists",
        NotificationKind.SourceStopped => "when one of your sources stops working",
        _ => "the weekly summary",
    };
}
