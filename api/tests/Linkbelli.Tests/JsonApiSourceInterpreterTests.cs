using Linkbelli.Application.Common;
using Linkbelli.Application.Sources;

namespace Linkbelli.Tests;

public class JsonApiSourceInterpreterTests
{
    private const string Json = """
        {
          "data": {
            "posts": [
              { "permalink": "https://example.com/1", "headline": "First" },
              { "permalink": "https://example.com/2", "headline": "Second" },
              { "headline": "No url, skipped" }
            ]
          }
        }
        """;

    [Fact]
    public void Extracts_links_via_jsonpath()
    {
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.data.posts[*]",
            [JsonApiSourceInterpreter.UrlPathKey] = "permalink",
            [JsonApiSourceInterpreter.TitlePathKey] = "headline",
        };

        var links = JsonApiSourceInterpreter.Parse(Json, config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://example.com/1", links[0].Url);
        Assert.Equal("First", links[0].Title);
        Assert.Equal("https://example.com/2", links[1].Url);
    }

    [Fact]
    public void Header_keys_are_secret_others_are_not()
    {
        var interpreter = new JsonApiSourceInterpreter(null!);

        Assert.True(interpreter.IsSecretConfigKey("header.Authorization"));
        Assert.True(interpreter.IsSecretConfigKey("HEADER.X-Api-Key"));
        Assert.False(interpreter.IsSecretConfigKey(JsonApiSourceInterpreter.UrlKey));
    }

    [Fact]
    public void UrlTemplate_expands_single_placeholder_from_item_field()
    {
        const string tmdbJson = """
            { "results": [ { "id": 123, "title": "A" }, { "id": 456, "title": "B" } ] }
            """;
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.results[*]",
            [JsonApiSourceInterpreter.UrlTemplateKey] = "https://www.themoviedb.org/movie/{id}",
            [JsonApiSourceInterpreter.TitlePathKey] = "title",
        };

        var links = JsonApiSourceInterpreter.Parse(tmdbJson, config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://www.themoviedb.org/movie/123", links[0].Url);
        Assert.Equal("A", links[0].Title);
        Assert.Equal("https://www.themoviedb.org/movie/456", links[1].Url);
    }

    [Fact]
    public void UrlTemplate_expands_multiple_placeholders_including_nested_paths()
    {
        const string redditJson = """
            {
              "posts": [
                { "subreddit": "movies", "id": "abc", "meta": { "slug": "great_film" } },
                { "subreddit": "books", "id": "xyz", "meta": { "slug": "great_book" } }
              ]
            }
            """;
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.posts[*]",
            [JsonApiSourceInterpreter.UrlTemplateKey] = "https://reddit.com/r/{subreddit}/comments/{id}/{meta.slug}",
        };

        var links = JsonApiSourceInterpreter.Parse(redditJson, config);

        Assert.Equal(2, links.Count);
        Assert.Equal("https://reddit.com/r/movies/comments/abc/great_film", links[0].Url);
        Assert.Equal("https://reddit.com/r/books/comments/xyz/great_book", links[1].Url);
    }

    [Fact]
    public void UrlTemplate_skips_items_with_any_missing_placeholder_value()
    {
        const string partialJson = """
            {
              "results": [
                { "id": 1, "slug": "ok" },
                { "id": 2 },
                { "id": 3, "slug": "" }
              ]
            }
            """;
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.results[*]",
            [JsonApiSourceInterpreter.UrlTemplateKey] = "https://example.com/{id}/{slug}",
        };

        var links = JsonApiSourceInterpreter.Parse(partialJson, config);

        Assert.Single(links);
        Assert.Equal("https://example.com/1/ok", links[0].Url);
    }

    [Fact]
    public void ValidateConfig_rejects_urlTemplate_without_placeholder()
    {
        var interpreter = new JsonApiSourceInterpreter(null!);
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.UrlKey] = "https://api.example.com/items",
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.results[*]",
            [JsonApiSourceInterpreter.UrlTemplateKey] = "https://x.example/movie/no-placeholders-here",
        };

        var ex = Assert.Throws<ValidationException>(() => interpreter.ValidateConfig(config));
        Assert.Contains($"config.{JsonApiSourceInterpreter.UrlTemplateKey}", ex.Errors.Keys);
    }

    [Fact]
    public void ValidateConfig_requires_urlPath_or_urlTemplate()
    {
        var interpreter = new JsonApiSourceInterpreter(null!);
        var config = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.UrlKey] = "https://api.example.com/items",
            [JsonApiSourceInterpreter.ItemsPathKey] = "$.results[*]",
        };

        var ex = Assert.Throws<ValidationException>(() => interpreter.ValidateConfig(config));
        Assert.Contains($"config.{JsonApiSourceInterpreter.UrlPathKey}", ex.Errors.Keys);
    }
}
