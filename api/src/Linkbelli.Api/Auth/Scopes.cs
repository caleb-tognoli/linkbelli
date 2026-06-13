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

    /// <summary>All scopes a key may be granted (e.g. for validation / docs).</summary>
    public static readonly string[] All =
        [PlaylistsRead, PlaylistsWrite, SourcesRead, SourcesWrite, LinksWrite];

    public const string PolicyPrefix = "scope:";

    /// <summary>Authorization policy name carrying a single scope requirement.</summary>
    public static string Policy(string scope) => PolicyPrefix + scope;
}
