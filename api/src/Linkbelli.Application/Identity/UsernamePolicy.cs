namespace Linkbelli.Application.Identity;

/// <summary>
/// What a username is allowed to be.
/// </summary>
/// <remarks>
/// A username is not a private handle here: it is a path segment on a public profile, it appears
/// in every syndicated feed, and sitemap.xml hands it to crawlers. Identity's defaults allow
/// <c>@</c> and <c>.</c>, and the sign-in field takes a username <em>or</em> an email — so people
/// typed their email address into the box and had it published. That is the reason this exists.
/// </remarks>
public static class UsernamePolicy
{
    /// <summary>
    /// Letters, digits, hyphen and underscore. No <c>@</c> and no dot, so an email address cannot
    /// be one, and nothing needs escaping in a URL.
    /// </summary>
    public const string AllowedCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";

    public const int MinLength = 3;
    public const int MaxLength = 30;

    /// <summary>
    /// Names that would collide with a route or impersonate the instance.
    /// </summary>
    /// <remarks>
    /// Public profiles live at <c>/public/{username}</c> so a collision is not currently possible
    /// by construction — but the reserved list costs nothing now and is very awkward to introduce
    /// after somebody has been using one of these for a year. The impersonation half is the part
    /// that actually matters: an "admin" or "support" profile on your own domain is a phishing
    /// primitive.
    /// </remarks>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // Routes, in case the profile path is ever shortened to /{username}.
        "admin", "api", "automations", "discover", "duplicates", "embed", "feed", "folders",
        "import", "login", "logout", "oembed", "playlists", "profile", "public", "queue", "read",
        "register", "save", "search", "sources", "trash", "unsubscribe", "robots", "sitemap",
        "auth", "backups", "export", "items", "links", "notifications", "tags", "inbox", "health",
        "metrics", "hangfire", "scalar", "openapi",
        // Things that should not appear to speak for the instance.
        "linkbelli", "support", "help", "security", "abuse", "root", "system", "moderator", "mod",
        "administrator", "official", "staff", "billing", "noreply", "no-reply", "postmaster",
    };

    /// <summary>
    /// Why this username cannot be used, or null when it can.
    /// </summary>
    /// <remarks>
    /// Returns the reason rather than a bool so the caller can say which rule was broken. People
    /// retype a rejected username; "3 to 30 characters" gets them there and "invalid" does not.
    /// </remarks>
    public static string? Validate(string? username)
    {
        var name = username?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            return "A username is required.";
        }

        if (name.Length is < MinLength or > MaxLength)
        {
            return $"A username must be between {MinLength} and {MaxLength} characters.";
        }

        foreach (var ch in name)
        {
            if (!AllowedCharacters.Contains(ch, StringComparison.Ordinal))
            {
                return "A username can use letters, numbers, hyphens and underscores. "
                    + "It is part of your public address, so it cannot be an email address.";
            }
        }

        // Leading and trailing punctuation reads as a mistake and makes two names look alike.
        if (name[0] is '-' or '_' || name[^1] is '-' or '_')
        {
            return "A username cannot start or end with a hyphen or underscore.";
        }

        if (Reserved.Contains(name))
        {
            return "That username is reserved.";
        }

        return null;
    }

    /// <summary>
    /// Whether an existing username satisfies the current rules.
    /// </summary>
    /// <remarks>
    /// Accounts created before this policy may not, and are deliberately left alone rather than
    /// locked out of their own library. What they are kept out of is anything that republishes
    /// the name — see the sitemap.
    /// </remarks>
    public static bool IsPublishable(string? username) => Validate(username) is null;
}
