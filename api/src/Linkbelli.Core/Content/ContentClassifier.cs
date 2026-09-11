namespace Linkbelli.Core.Content;

/// <summary>
/// Works out what a link is from what is already known about it: the address, what the page said
/// about itself, what was served, and how much prose was found. Pure arithmetic over those
/// signals, so it can be tested without a page and re-run over rows saved before it existed.
/// </summary>
public static class ContentClassifier
{
    /// <summary>Prose this long is an article whatever else the page claims to be.</summary>
    public const int ArticleWords = 250;

    private static readonly string[] VideoHosts =
    [
        "youtube.com", "youtu.be", "vimeo.com", "dailymotion.com", "twitch.tv",
        "ted.com", "odysee.com", "rumble.com", "bitchute.com", "nebula.tv",
    ];

    private static readonly string[] RepositoryHosts =
    [
        "github.com", "gitlab.com", "bitbucket.org", "codeberg.org", "sr.ht", "sourcehut.org",
    ];

    private static readonly string[] PaperHosts =
    [
        "arxiv.org", "doi.org", "pubmed.ncbi.nlm.nih.gov", "ncbi.nlm.nih.gov", "jstor.org",
        "sciencedirect.com", "springer.com", "nature.com", "acm.org", "ieee.org",
        "biorxiv.org", "medrxiv.org", "ssrn.com", "researchgate.net", "semanticscholar.org",
    ];

    private static readonly string[] AudioHosts =
    [
        "soundcloud.com", "bandcamp.com", "podcasts.apple.com", "pca.st", "overcast.fm",
        "spotify.com", "open.spotify.com", "mixcloud.com",
    ];

    private static readonly string[] SocialHosts =
    [
        "twitter.com", "x.com", "mastodon.social", "bsky.app", "reddit.com", "old.reddit.com",
        "news.ycombinator.com", "lobste.rs", "threads.net", "facebook.com", "instagram.com",
        "linkedin.com", "tumblr.com",
    ];

    private static readonly string[] ImageHosts = ["imgur.com", "flickr.com", "unsplash.com"];

    /// <summary>
    /// What this link is.
    /// </summary>
    /// <param name="url">The canonical URL.</param>
    /// <param name="host">Its hostname, already lowercased.</param>
    /// <param name="ogType">The page's own <c>og:type</c>, when it declared one.</param>
    /// <param name="mediaType">The content type that came back, when it wasn't HTML.</param>
    /// <param name="wordCount">Words of prose found on the page, if any were.</param>
    public static ContentKind Classify(
        string url, string host, string? ogType = null, string? mediaType = null, int? wordCount = null)
    {
        // What was actually served settles it: a PDF is a PDF whatever the address suggests.
        if (mediaType is not null)
        {
            var served = FromMediaType(mediaType);
            if (served != ContentKind.Unknown)
            {
                return served;
            }
        }

        var path = PathOf(url);
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            return ContentKind.Document;
        }

        // The host is the strongest remaining signal, and it is the one that still works for a
        // page that was never fetched — which is what lets old rows be classified at all.
        var fromHost = FromHost(host);
        if (fromHost != ContentKind.Unknown)
        {
            // A repository host also serves issues and pull requests, but calling those anything
            // other than the repository they belong to helps nobody find them.
            return fromHost;
        }

        if (ogType is not null)
        {
            var declared = FromOgType(ogType);
            if (declared != ContentKind.Unknown)
            {
                return declared;
            }
        }

        // Last: enough prose on the page to be worth reading. Deliberately after the host checks,
        // because a video page with a long description is still a video.
        return wordCount >= ArticleWords ? ContentKind.Article : ContentKind.Unknown;
    }

    private static ContentKind FromMediaType(string mediaType)
    {
        if (mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return ContentKind.Video;
        if (mediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return ContentKind.Audio;
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ContentKind.Image;

        return mediaType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("epub", StringComparison.OrdinalIgnoreCase)
                ? ContentKind.Document
                : ContentKind.Unknown;
    }

    private static ContentKind FromOgType(string ogType)
    {
        if (ogType.StartsWith("video", StringComparison.OrdinalIgnoreCase)) return ContentKind.Video;
        if (ogType.StartsWith("music", StringComparison.OrdinalIgnoreCase)) return ContentKind.Audio;
        if (ogType.Equals("image", StringComparison.OrdinalIgnoreCase)) return ContentKind.Image;

        return ogType.StartsWith("article", StringComparison.OrdinalIgnoreCase)
            ? ContentKind.Article
            : ContentKind.Unknown;
    }

    private static ContentKind FromHost(string host)
    {
        if (Matches(host, VideoHosts)) return ContentKind.Video;
        if (Matches(host, RepositoryHosts)) return ContentKind.Repository;
        if (Matches(host, PaperHosts)) return ContentKind.Paper;
        if (Matches(host, AudioHosts)) return ContentKind.Audio;
        if (Matches(host, SocialHosts)) return ContentKind.Social;
        if (Matches(host, ImageHosts)) return ContentKind.Image;

        return ContentKind.Unknown;
    }

    /// <summary>
    /// Matches the host itself or any subdomain of it, so <c>m.youtube.com</c> counts and
    /// <c>notyoutube.com</c> doesn't.
    /// </summary>
    private static bool Matches(string host, string[] known) =>
        known.Any(candidate =>
            host.Equals(candidate, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase));

    /// <summary>The path, without a query string that often carries an unrelated ".pdf".</summary>
    private static string PathOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.AbsolutePath : url;
}
