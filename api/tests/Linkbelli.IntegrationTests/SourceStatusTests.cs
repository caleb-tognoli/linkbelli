using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Pausing is a status on the source, not a cron that never fires. These pin the two properties
/// that mattered: the owner's cadence survives a pause, and a paused source is off the schedule.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceStatusTests(PostgresApiFactory factory)
{
    private record StatusSourceDto(
        Guid Id, string Name, string Type, string Schedule, string Visibility, string Status);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<StatusSourceDto> CreateSourceAsync(HttpClient client, string schedule)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Status source",
            type = "Rss",
            config = new { feedUrl = "https://status.example/feed.xml" },
            schedule,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StatusSourceDto>())!;
    }

    [Fact]
    public async Task A_new_source_is_active()
    {
        var client = await NewUserAsync();
        var source = await CreateSourceAsync(client, "*/30 * * * *");

        Assert.Equal("Active", source.Status);
    }

    [Fact]
    public async Task Pausing_keeps_the_schedule_so_resuming_restores_the_same_cadence()
    {
        var client = await NewUserAsync();
        var source = await CreateSourceAsync(client, "*/15 * * * *");

        var paused = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Paused" });
        paused.EnsureSuccessStatusCode();
        var afterPause = (await paused.Content.ReadFromJsonAsync<StatusSourceDto>())!;

        Assert.Equal("Paused", afterPause.Status);
        Assert.Equal("*/15 * * * *", afterPause.Schedule);

        var resumed = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Active" });
        resumed.EnsureSuccessStatusCode();
        var afterResume = (await resumed.Content.ReadFromJsonAsync<StatusSourceDto>())!;

        Assert.Equal("Active", afterResume.Status);
        Assert.Equal("*/15 * * * *", afterResume.Schedule);
    }

    [Fact]
    public async Task Pausing_removes_the_recurring_job_and_resuming_puts_it_back()
    {
        var client = await NewUserAsync();
        var source = await CreateSourceAsync(client, "0 * * * *");
        var jobId = $"source:{source.Id}";

        Assert.True(await RecurringJobExistsAsync(jobId));

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Paused" }))
            .EnsureSuccessStatusCode();
        Assert.False(await RecurringJobExistsAsync(jobId));

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Active" }))
            .EnsureSuccessStatusCode();
        Assert.True(await RecurringJobExistsAsync(jobId));
    }

    [Fact]
    public async Task An_unrelated_update_leaves_the_status_alone()
    {
        var client = await NewUserAsync();
        var source = await CreateSourceAsync(client, "0 * * * *");

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Paused" }))
            .EnsureSuccessStatusCode();

        var renamed = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { name = "Renamed" });
        renamed.EnsureSuccessStatusCode();
        var after = (await renamed.Content.ReadFromJsonAsync<StatusSourceDto>())!;

        Assert.Equal("Renamed", after.Name);
        Assert.Equal("Paused", after.Status);
        Assert.False(await RecurringJobExistsAsync($"source:{source.Id}"));
    }

    [Fact]
    public async Task A_paused_source_can_still_be_run_on_demand()
    {
        var client = await NewUserAsync();
        var source = await CreateSourceAsync(client, "0 * * * *");

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}", new { status = "Paused" }))
            .EnsureSuccessStatusCode();

        var run = await client.PostAsync($"/api/v1/sources/{source.Id}/run", null);
        Assert.Equal(HttpStatusCode.Accepted, run.StatusCode);
    }

    /// <summary>Hangfire keeps recurring jobs in its own Postgres set; read it straight.</summary>
    private async Task<bool> RecurringJobExistsAsync(string jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM hangfire.set WHERE key = 'recurring-jobs' AND value = @id""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "id";
        parameter.Value = jobId;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }
}
