using System.Text.Json;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>One value a template asks the person for.</summary>
public record TemplateField(
    string Key,
    string Label,
    string? Placeholder = null,
    string? Help = null,
    bool Required = true);

/// <summary>
/// The templates that ship with the app. These are the services people actually point a source
/// at, with the part that takes research — a feed path, a JSON shape — already worked out.
///
/// Re-seeded on startup, so a fix here reaches everyone who used the template.
/// </summary>
public static class BuiltinSourceTemplates
{
    public static IReadOnlyList<SourceTemplate> All { get; } =
    [
        Template(
            key: "youtube-channel",
            name: "YouTube channel",
            description: "New uploads from one channel. Find the channel id in the page source, or paste a handle URL into a converter.",
            type: SourceType.Rss,
            schedule: "0 */2 * * *",
            config: new()
            {
                ["feedUrl"] = "https://www.youtube.com/feeds/videos.xml?channel_id={{channelId}}",
            },
            fields:
            [
                new TemplateField("channelId", "Channel ID", "UCBa659QWEk1AI4Tg--mrJ2A",
                    "The id starting with UC, not the @handle."),
            ]),

        Template(
            key: "youtube-playlist",
            name: "YouTube playlist",
            description: "New additions to a YouTube playlist.",
            type: SourceType.Rss,
            schedule: "0 */2 * * *",
            config: new()
            {
                ["feedUrl"] = "https://www.youtube.com/feeds/videos.xml?playlist_id={{playlistId}}",
            },
            fields:
            [
                new TemplateField("playlistId", "Playlist ID", "PLxxxxxxxx",
                    "From the list= part of the playlist's address."),
            ]),

        Template(
            key: "reddit-subreddit",
            name: "Subreddit",
            description: "New posts in a subreddit.",
            type: SourceType.Rss,
            schedule: "*/30 * * * *",
            config: new()
            {
                ["feedUrl"] = "https://www.reddit.com/r/{{subreddit}}/new/.rss",
            },
            fields:
            [
                new TemplateField("subreddit", "Subreddit", "programming", "Without the r/ prefix."),
            ]),

        Template(
            key: "hacker-news-top",
            name: "Hacker News",
            description: "Stories from Hacker News above a score you choose.",
            type: SourceType.Rss,
            schedule: "0 * * * *",
            config: new()
            {
                ["feedUrl"] = "https://hnrss.org/newest?points={{minPoints}}",
            },
            fields:
            [
                new TemplateField("minPoints", "Minimum score", "100",
                    "Only stories that have reached this many points."),
            ]),

        Template(
            key: "github-releases",
            name: "GitHub releases",
            description: "New releases of one repository.",
            type: SourceType.Rss,
            schedule: "0 */6 * * *",
            config: new()
            {
                ["feedUrl"] = "https://github.com/{{owner}}/{{repo}}/releases.atom",
            },
            fields:
            [
                new TemplateField("owner", "Owner", "dotnet", "The user or organisation."),
                new TemplateField("repo", "Repository", "runtime"),
            ]),

        Template(
            key: "github-commits",
            name: "GitHub commits",
            description: "Commits on a branch of one repository.",
            type: SourceType.Rss,
            schedule: "0 */6 * * *",
            config: new()
            {
                ["feedUrl"] = "https://github.com/{{owner}}/{{repo}}/commits/{{branch}}.atom",
            },
            fields:
            [
                new TemplateField("owner", "Owner", "dotnet"),
                new TemplateField("repo", "Repository", "runtime"),
                new TemplateField("branch", "Branch", "main"),
            ]),

        Template(
            key: "podcast",
            name: "Podcast",
            description: "New episodes of a podcast, from its feed address.",
            type: SourceType.Rss,
            schedule: "0 */4 * * *",
            config: new()
            {
                ["feedUrl"] = "{{feedUrl}}",
            },
            fields:
            [
                new TemplateField("feedUrl", "Feed address", "https://example.com/podcast.xml",
                    "Usually linked from the show's site as RSS."),
            ]),

        Template(
            key: "mastodon-hashtag",
            name: "Mastodon hashtag",
            description: "Posts carrying a hashtag on one Mastodon server.",
            type: SourceType.Rss,
            schedule: "*/30 * * * *",
            config: new()
            {
                ["feedUrl"] = "https://{{server}}/tags/{{hashtag}}.rss",
            },
            fields:
            [
                new TemplateField("server", "Server", "mastodon.social", "Without https://."),
                new TemplateField("hashtag", "Hashtag", "dotnet", "Without the #."),
            ]),

        Template(
            key: "generic-rss",
            name: "Any feed",
            description: "A plain RSS or Atom feed, when you already have its address.",
            type: SourceType.Rss,
            schedule: "0 * * * *",
            config: new()
            {
                ["feedUrl"] = "{{feedUrl}}",
            },
            fields:
            [
                new TemplateField("feedUrl", "Feed address", "https://example.com/rss.xml"),
            ]),
    ];

    private static SourceTemplate Template(
        string key,
        string name,
        string description,
        SourceType type,
        string schedule,
        Dictionary<string, string> config,
        TemplateField[] fields) => new()
        {
            Key = key,
            Name = name,
            Description = description,
            Type = type,
            SuggestedSchedule = schedule,
            BaseConfig = JsonSerializer.Serialize(config),
            Fields = JsonSerializer.Serialize(fields),
            Builtin = true,
        };
}
