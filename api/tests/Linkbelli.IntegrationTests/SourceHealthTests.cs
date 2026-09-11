using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Every run was already being recorded and none of it was ever shown. These cover the summary
/// that turns those rows into the two questions an owner actually asks: is it working, and is it
/// still finding anything?
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceHealthTests(PostgresApiFactory factory)
{
    /// <summary>Stands in for the RSS interpreter, returning exactly what a test asks it for.</summary>
    private sealed class ScriptedInterpreter : ISourceInterpreter
    {
        /// <summary>Links to return on the next run; null throws instead.</summary>
        public static string[]? Next { get; set; } = [];

        public SourceType Type => SourceType.Rss;

        public void ValidateConfig(IReadOnlyDictionary<string, string> config) { }

        public Task<SourceFetchResult> FetchAsync(
            IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
        {
            if (Next is null)
            {
                throw new InvalidOperationException("Selector matched nothing.");
            }

            return Task.FromResult(new SourceFetchResult(
                [.. Next.Select(url => new DiscoveredLink(url, Title: null))]));
        }
    }

    private record HealthDto(
        int Runs, int WindowDays, int Succeeded, int Failed,
        int? SuccessRate, double? AverageFound, double? AverageAdded,
        int EmptyRuns, int ConsecutiveFailures,
        DateTimeOffset? LastRunAt, string? LastRunStatus, string? LastError);

    private record SourceDto(Guid Id, string Name);

    private WebApplicationFactory<Program> WithScripted() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISourceInterpreter>();
            services.AddSingleton<ISourceInterpreter, ScriptedInterpreter>();
        }));

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<SourceDto> NewSourceAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Scripted source",
            type = "Rss",
            config = new { feedUrl = $"https://scripted.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 * * * *",
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SourceDto>())!;
    }

    /// <summary>Runs the source once per scripted entry, so each run returns its own links.</summary>
    private static async Task RunAsync(WebApplicationFactory<Program> app, Guid sourceId, params string[]?[] script)
    {
        foreach (var entry in script)
        {
            ScriptedInterpreter.Next = entry;

            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISourceRunner>().RunAsync(sourceId);
        }
    }

    private static async Task<HealthDto> HealthAsync(HttpClient client, Guid sourceId) =>
        (await client.GetFromJsonAsync<HealthDto>($"/api/v1/sources/{sourceId}/health"))!;

    [Fact]
    public async Task A_source_that_has_never_run_reports_nothing_rather_than_zero()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        var health = await HealthAsync(client, source.Id);

        Assert.Equal(0, health.Runs);
        // Null, not 0%: a source nobody has run yet is not failing, and a red 0% would say it is.
        Assert.Null(health.SuccessRate);
        Assert.Null(health.AverageFound);
        Assert.Null(health.LastRunAt);
        Assert.Equal(30, health.WindowDays);
    }

    [Fact]
    public async Task Runs_are_split_into_successes_and_failures_with_a_rate()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        var host = Guid.NewGuid().ToString("N");
        await RunAsync(app, source.Id,
            [$"https://{host}.example/a", $"https://{host}.example/b"],
            null,
            [$"https://{host}.example/c"],
            null);

        var health = await HealthAsync(client, source.Id);

        Assert.Equal(4, health.Runs);
        Assert.Equal(2, health.Succeeded);
        Assert.Equal(2, health.Failed);
        Assert.Equal(50, health.SuccessRate);
        Assert.Equal("Failed", health.LastRunStatus);
        Assert.Equal("Selector matched nothing.", health.LastError);
        Assert.NotNull(health.LastRunAt);
    }

    [Fact]
    public async Task Averages_ignore_failed_runs()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        var host = Guid.NewGuid().ToString("N");
        await RunAsync(app, source.Id,
            [$"https://{host}.example/a", $"https://{host}.example/b", $"https://{host}.example/c"],
            null,
            [$"https://{host}.example/d"]);

        var health = await HealthAsync(client, source.Id);

        // (3 + 1) / 2, not (3 + 0 + 1) / 3: a failed run found nothing because it failed, and
        // averaging it in would quietly drag every figure on the page down.
        Assert.Equal(2.0, health.AverageFound);
        Assert.Equal(2.0, health.AverageAdded);
    }

    [Fact]
    public async Task A_succeeding_source_that_finds_nothing_is_counted_separately()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        var host = Guid.NewGuid().ToString("N");
        await RunAsync(app, source.Id,
            [$"https://{host}.example/a"],
            [],
            []);

        var health = await HealthAsync(client, source.Id);

        // 100% green, and yet it has returned nothing twice running: the exact shape of a scraper
        // whose selector stopped matching, which no run status could ever reveal on its own.
        Assert.Equal(100, health.SuccessRate);
        Assert.Equal(2, health.EmptyRuns);
        Assert.Equal(0, health.ConsecutiveFailures);
    }

    [Fact]
    public async Task Repeat_finds_count_as_found_but_not_as_added()
    {
        using var app = WithScripted();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        var host = Guid.NewGuid().ToString("N");
        string[] same = [$"https://{host}.example/a", $"https://{host}.example/b"];
        await RunAsync(app, source.Id, same, same);

        var health = await HealthAsync(client, source.Id);

        // A feed that has not updated still finds its two items; only the first run added them.
        Assert.Equal(2.0, health.AverageFound);
        Assert.Equal(1.0, health.AverageAdded);
        Assert.Equal(0, health.EmptyRuns);
    }

    [Fact]
    public async Task Another_account_cannot_read_it()
    {
        using var app = WithScripted();
        var owner = await SignedInAsync(app);
        var source = await NewSourceAsync(owner);
        await RunAsync(app, source.Id, []);

        var stranger = await SignedInAsync(app);
        var res = await stranger.GetAsync($"/api/v1/sources/{source.Id}/health");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
