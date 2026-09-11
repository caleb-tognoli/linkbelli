using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Ready-made source configs. The value is that the person supplies only what is genuinely
/// theirs — a channel id, a subreddit — and never has to work out a feed path.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceTemplateTests(PostgresApiFactory factory)
{
    private record FieldDto(string Key, string Label, string? Placeholder, string? Help, bool Required);

    private record TemplateDto(
        Guid Id, string? Key, string Name, string Description, string Type,
        string? SuggestedSchedule, bool Builtin, List<FieldDto> Fields);

    private record CreatedSourceDto(
        Guid Id, string Name, string Type, Dictionary<string, string> Config, string Schedule);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<List<TemplateDto>> TemplatesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TemplateDto>>("/api/v1/sources/templates"))!;

    private static async Task<TemplateDto> TemplateAsync(HttpClient client, string key) =>
        (await TemplatesAsync(client)).Single(t => t.Key == key);

    [Fact]
    public async Task The_built_in_templates_are_seeded_at_startup()
    {
        var client = await NewUserAsync();

        var templates = await TemplatesAsync(client);

        Assert.NotEmpty(templates);
        Assert.All(templates, t => Assert.True(t.Builtin));
        Assert.Contains(templates, t => t.Key == "youtube-channel");
        Assert.Contains(templates, t => t.Key == "github-releases");
        Assert.Contains(templates, t => t.Key == "reddit-subreddit");
    }

    [Fact]
    public async Task A_template_says_what_it_needs_from_the_person()
    {
        var client = await NewUserAsync();

        var github = await TemplateAsync(client, "github-releases");

        // Two fields, and nothing about feed paths — that part is already done.
        Assert.Equal(["owner", "repo"], github.Fields.Select(f => f.Key).ToArray());
        Assert.Equal("Owner", github.Fields[0].Label);
        Assert.NotNull(github.SuggestedSchedule);
    }

    [Fact]
    public async Task A_source_created_from_a_template_gets_the_rendered_config()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "github-releases");

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "dotnet/runtime releases",
            templateId = template.Id,
            variables = new { owner = "dotnet", repo = "runtime" },
        });
        res.EnsureSuccessStatusCode();
        var source = (await res.Content.ReadFromJsonAsync<CreatedSourceDto>())!;

        Assert.Equal("Rss", source.Type);
        Assert.Equal("https://github.com/dotnet/runtime/releases.atom", source.Config["feedUrl"]);
    }

    [Fact]
    public async Task The_templates_suggested_schedule_is_used_when_none_is_given()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "github-releases");

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Releases",
            templateId = template.Id,
            variables = new { owner = "dotnet", repo = "runtime" },
        });
        res.EnsureSuccessStatusCode();

        var source = (await res.Content.ReadFromJsonAsync<CreatedSourceDto>())!;
        Assert.Equal(template.SuggestedSchedule, source.Schedule);
    }

    [Fact]
    public async Task An_explicit_schedule_still_wins()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "github-releases");

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Releases, hourly",
            templateId = template.Id,
            variables = new { owner = "dotnet", repo = "runtime" },
            schedule = "0 * * * *",
        });
        res.EnsureSuccessStatusCode();

        Assert.Equal("0 * * * *", (await res.Content.ReadFromJsonAsync<CreatedSourceDto>())!.Schedule);
    }

    [Fact]
    public async Task A_missing_value_is_named_rather_than_left_to_fail_later()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "github-releases");

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Half filled",
            templateId = template.Id,
            variables = new { owner = "dotnet" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);

        // Named by its label, not its placeholder key — the person never saw "{{repo}}".
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Repository", body);
    }

    [Fact]
    public async Task A_rendered_config_still_faces_the_interpreters_own_validation()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "generic-rss");

        // The template renders whatever it is given; the RSS interpreter still requires a real
        // http(s) address, so a template cannot smuggle a bad config through.
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Not a feed",
            templateId = template.Id,
            variables = new { feedUrl = "not a url at all" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task An_unknown_template_is_not_found()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Nowhere",
            templateId = Guid.NewGuid(),
            variables = new { anything = "x" },
        });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task A_source_can_still_be_created_without_a_template()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Hand written",
            type = "Rss",
            config = new { feedUrl = "https://handwritten.example/feed.xml" },
            schedule = "0 * * * *",
        });

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_value_is_trimmed_before_it_reaches_the_config()
    {
        var client = await NewUserAsync();
        var template = await TemplateAsync(client, "reddit-subreddit");

        // People paste values with stray whitespace; a URL with a space in it fails obscurely.
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Padded",
            templateId = template.Id,
            variables = new { subreddit = "  programming  " },
        });
        res.EnsureSuccessStatusCode();

        var source = (await res.Content.ReadFromJsonAsync<CreatedSourceDto>())!;
        Assert.Equal("https://www.reddit.com/r/programming/new/.rss", source.Config["feedUrl"]);
    }
}
