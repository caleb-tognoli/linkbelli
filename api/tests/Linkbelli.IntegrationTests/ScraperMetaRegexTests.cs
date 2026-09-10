using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// End-to-end config plumbing for the scraper's <c>meta.&lt;name&gt;.regex</c> /
/// <c>.replacement</c> keys: they must survive create, read-back and update byte-for-byte
/// (backslashes and <c>$1</c> intact, never mistaken for secrets), and an unparseable pattern
/// must be rejected at the API boundary rather than at run time.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ScraperMetaRegexTests(PostgresApiFactory factory)
{
    private record SourceWithConfigDto(Guid Id, string Name, string Type, Dictionary<string, string> Config);

    private const string TitleRegex = @"\s*\|\s*Site Name$";
    private const string AuthorRegex = "^Posted by (.+)$";

    private static async Task<HttpClient> NewUserAsync(PostgresApiFactory factory)
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object ScraperBody(string name, Dictionary<string, string> config) => new
    {
        name,
        type = "Scraper",
        config,
        schedule = "*/15 * * * *",
    };

    private static Dictionary<string, string> ValidConfig() => new()
    {
        ["url"] = "https://news.example/section",
        ["itemSelector"] = "li.story",
        ["linkSelector"] = "a.headline",
        ["meta.title"] = "a.headline",
        ["meta.title.regex"] = TitleRegex,
        ["meta.author"] = ".byline",
        ["meta.author.regex"] = AuthorRegex,
        ["meta.author.replacement"] = "$1",
    };

    [Fact]
    public async Task Create_preserves_regex_keys_verbatim_on_read_back()
    {
        var user = await NewUserAsync(factory);

        var created = await (await user.PostAsJsonAsync("/api/v1/sources", ScraperBody("Regex scraper", ValidConfig())))
            .Content.ReadFromJsonAsync<SourceWithConfigDto>();

        // Re-fetch rather than trusting the create response, so storage + redaction are both covered.
        var fetched = await user.GetFromJsonAsync<SourceWithConfigDto>($"/api/v1/sources/{created!.Id}");

        Assert.Equal(TitleRegex, fetched!.Config["meta.title.regex"]);
        Assert.Equal(AuthorRegex, fetched.Config["meta.author.regex"]);
        Assert.Equal("$1", fetched.Config["meta.author.replacement"]);
        // Not secrets — they must not come back redacted.
        Assert.DoesNotContain("***", fetched.Config.Values);
    }

    [Fact]
    public async Task Invalid_regex_is_rejected_at_the_api_boundary()
    {
        var user = await NewUserAsync(factory);
        var config = ValidConfig();
        config["meta.title.regex"] = "([unclosed";

        var response = await user.PostAsJsonAsync("/api/v1/sources", ScraperBody("Bad regex", config));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // Specific enough that an unrelated 400 can't satisfy it.
        Assert.Contains("config.meta.title.regex", body);
        Assert.Contains("Invalid regular expression", body);
    }

    [Fact]
    public async Task Patch_updates_and_removes_regex_keys()
    {
        var user = await NewUserAsync(factory);
        var created = await (await user.PostAsJsonAsync("/api/v1/sources", ScraperBody("Patchable", ValidConfig())))
            .Content.ReadFromJsonAsync<SourceWithConfigDto>();

        // Config is replaced wholesale: a narrowed config both changes one pattern and drops the rest.
        var updated = new Dictionary<string, string>
        {
            ["url"] = "https://news.example/section",
            ["itemSelector"] = "li.story",
            ["meta.title"] = "a.headline",
            ["meta.title.regex"] = @"\s+—\s+Site Name$",
        };
        var patch = await user.PatchAsJsonAsync($"/api/v1/sources/{created!.Id}", new { config = updated });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var fetched = await user.GetFromJsonAsync<SourceWithConfigDto>($"/api/v1/sources/{created.Id}");

        Assert.Equal(@"\s+—\s+Site Name$", fetched!.Config["meta.title.regex"]);
        Assert.False(fetched.Config.ContainsKey("meta.author.regex"));
        Assert.False(fetched.Config.ContainsKey("meta.author.replacement"));
    }

    [Fact]
    public async Task Preview_rejects_an_invalid_regex_before_fetching()
    {
        var user = await NewUserAsync(factory);
        var config = ValidConfig();
        config["meta.author.regex"] = "(?<bad";

        var response = await user.PostAsJsonAsync("/api/v1/sources/preview", new { type = "Scraper", config });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("config.meta.author.regex", body);
        Assert.Contains("Invalid regular expression", body);
    }
}
