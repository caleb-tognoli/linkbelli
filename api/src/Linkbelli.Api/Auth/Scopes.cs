namespace Linkbelli.Api.Auth;

/// <summary>
/// API-key permission scopes. A key with <b>no</b> scopes is unrestricted (full access for its
/// owner); once a key lists any scopes, it is limited to exactly those. Interactive bearer
/// principals are never scope-limited.
/// </summary>
public static class Scopes
{
    public const string PlaylistsRead = "playlists:read";
    public const string PlaylistsWrite = "playlists:write";
    public const string SourcesRead = "sources:read";
    public const string SourcesWrite = "sources:write";
    public const string LinksWrite = "links:write";

    /// <summary>
    /// Reading the instance: the overview, the audit trail, the moderation queue.
    /// </summary>
    /// <remarks>
    /// A scope is not a promotion. Holding this lets a key reach the admin endpoints; the key's
    /// owner still has to be an admin, which is checked separately and cannot be granted here.
    /// </remarks>
    public const string AdminRead = "admin:read";

    /// <summary>Acting on it: blocking a host, setting a quota, taking something down.</summary>
    public const string AdminWrite = "admin:write";

    /// <summary>All scopes a key may be granted (e.g. for validation / docs).</summary>
    public static readonly string[] All =
        [PlaylistsRead, PlaylistsWrite, SourcesRead, SourcesWrite, LinksWrite, AdminRead, AdminWrite];

    public const string PolicyPrefix = "scope:";

    /// <summary>Authorization policy name carrying a single scope requirement.</summary>
    public static string Policy(string scope) => PolicyPrefix + scope;
}
