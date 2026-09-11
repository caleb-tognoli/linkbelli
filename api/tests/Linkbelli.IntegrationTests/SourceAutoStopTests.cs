using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// A scraper whose selector broke fails identically on every run. These cover the counting and
/// the point at which the source stops scheduling itself instead of failing forever in silence.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceAutoStopTests(PostgresApiFactory factory)
{
    /// <summary>Stands in for the RSS interpreter, and fails on demand.</summary>
    private sealed class FlakyInterpreter : ISourceInterpreter
    {
        public static bool ShouldFail { get; set; } = true;

        public SourceType Type => SourceType.Rss;

        public void ValidateConfig(IReadOnlyDictionary<string, string> config) { }

        public Task<SourceFetchResult> FetchAsync(
            IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new InvalidOperationException("Selector matched nothing.");
            }

            return Task.FromResult(new SourceFetchResult([]));
        }
    }

    private record StatusSourceDto(Guid Id, string Name, string Status, int ConsecutiveFailures);

    private WebApplicationFactory<Program> WithFlaky() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISourceInterpreter>();
            services.AddSingleton<ISourceInterpreter, FlakyInterpreter>();
        }));

    private static async Task<StatusSourceDto> NewSourceAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Flaky source",
            type = "Rss",
            config = new { feedUrl = $"https://flaky.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 * * * *",
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StatusSourceDto>())!;
    }

    private static async Task RunAsync(WebApplicationFactory<Program> app, Guid sourceId, int times)
    {
        for (var n = 0; n < times; n++)
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ISourceRunner>().RunAsync(sourceId);
        }
    }

    private static async Task<StatusSourceDto> GetAsync(HttpClient client, Guid sourceId) =>
        (await client.GetFromJsonAsync<StatusSourceDto>($"/api/v1/sources/{sourceId}"))!;

    private async Task<bool> RecurringJobExistsAsync(Guid sourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = @id""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = $"source:{sourceId}";
        command.Parameters.Add(parameter);

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Failures_are_counted_and_a_success_clears_the_streak()
    {
        using var app = WithFlaky();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        FlakyInterpreter.ShouldFail = true;
        await RunAsync(app, source.Id, 2);
        Assert.Equal(2, (await GetAsync(client, source.Id)).ConsecutiveFailures);

        FlakyInterpreter.ShouldFail = false;
        await RunAsync(app, source.Id, 1);
        Assert.Equal(0, (await GetAsync(client, source.Id)).ConsecutiveFailures);
    }

    [Fact]
    public async Task A_source_stops_itself_once_the_failures_pile_up()
    {
        using var app = WithFlaky();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        Assert.True(await RecurringJobExistsAsync(source.Id));

        FlakyInterpreter.ShouldFail = true;
        await RunAsync(app, source.Id, Source.FailureThreshold);

        var stopped = await GetAsync(client, source.Id);
        Assert.Equal("Failing", stopped.Status);
        Assert.Equal(Source.FailureThreshold, stopped.ConsecutiveFailures);

        // Off the schedule, so it stops burning the daily run quota on the same error.
        Assert.False(await RecurringJobExistsAsync(source.Id));
    }

    [Fact]
    public async Task Failing_is_distinct_from_a_pause_the_owner_chose()
    {
        using var app = WithFlaky();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Paused" }))
            .EnsureSuccessStatusCode();

        FlakyInterpreter.ShouldFail = true;
        await RunAsync(app, source.Id, Source.FailureThreshold);

        // Still Paused: the owner turned this off, and the system shouldn't relabel their choice.
        Assert.Equal("Paused", (await GetAsync(client, source.Id)).Status);
    }

    [Fact]
    public async Task Resuming_clears_the_streak_so_one_more_failure_does_not_stop_it_again()
    {
        using var app = WithFlaky();
        var client = await SignedInAsync(app);
        var source = await NewSourceAsync(client);

        FlakyInterpreter.ShouldFail = true;
        await RunAsync(app, source.Id, Source.FailureThreshold);
        Assert.Equal("Failing", (await GetAsync(client, source.Id)).Status);

        var resumed = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Active" });
        resumed.EnsureSuccessStatusCode();

        var after = (await resumed.Content.ReadFromJsonAsync<StatusSourceDto>())!;
        Assert.Equal("Active", after.Status);
        Assert.Equal(0, after.ConsecutiveFailures);
        Assert.True(await RecurringJobExistsAsync(source.Id));

        // One more failure is nowhere near the threshold again.
        await RunAsync(app, source.Id, 1);
        Assert.Equal("Active", (await GetAsync(client, source.Id)).Status);
    }
}
