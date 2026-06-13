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
}
