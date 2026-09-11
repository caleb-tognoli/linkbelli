using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Schedules used to be read in UTC with no way to say otherwise. These cover setting a zone,
/// rejecting one the server can't honour, and the zone actually reaching Hangfire.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceTimeZoneApiTests(PostgresApiFactory factory)
{
    private record ZonedSourceDto(Guid Id, string Name, string Schedule, string? TimeZone);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string? timeZone) =>
        client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = "Zoned source",
            type = "Rss",
            config = new { feedUrl = $"https://zoned.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 8 * * *",
            timeZone,
        });

    [Fact]
    public async Task A_source_can_be_scheduled_in_a_real_time_zone()
    {
        var client = await NewUserAsync();

        var res = await CreateAsync(client, "Europe/Rome");
        res.EnsureSuccessStatusCode();
        var source = (await res.Content.ReadFromJsonAsync<ZonedSourceDto>())!;

        Assert.Equal("Europe/Rome", source.TimeZone);
        Assert.Equal("0 8 * * *", source.Schedule);
    }

    [Fact]
    public async Task Omitting_a_zone_leaves_the_schedule_in_utc()
    {
        var client = await NewUserAsync();

        var res = await CreateAsync(client, null);
        res.EnsureSuccessStatusCode();

        Assert.Null((await res.Content.ReadFromJsonAsync<ZonedSourceDto>())!.TimeZone);
    }

    [Fact]
    public async Task A_zone_the_server_does_not_know_is_rejected_not_ignored()
    {
        var client = await NewUserAsync();

        var res = await CreateAsync(client, "Mars/Olympus_Mons");

        // Silently falling back to UTC would run the schedule at the wrong hour with no sign of it.
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("time zone", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_zone_can_be_changed_later()
    {
        var client = await NewUserAsync();
        var created = await CreateAsync(client, "Europe/Rome");
        created.EnsureSuccessStatusCode();
        var source = (await created.Content.ReadFromJsonAsync<ZonedSourceDto>())!;

        var updated = await client.PatchAsJsonAsync($"/api/v1/sources/{source.Id}",
            new { timeZone = "Asia/Tokyo" });
        updated.EnsureSuccessStatusCode();

        Assert.Equal("Asia/Tokyo", (await updated.Content.ReadFromJsonAsync<ZonedSourceDto>())!.TimeZone);
    }

    [Fact]
    public async Task The_zone_reaches_the_recurring_job()
    {
        var client = await NewUserAsync();
        var created = await CreateAsync(client, "Asia/Tokyo");
        created.EnsureSuccessStatusCode();
        var source = (await created.Content.ReadFromJsonAsync<ZonedSourceDto>())!;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT field, value FROM hangfire.hash WHERE key = @key""";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = $"recurring-job:source:{source.Id}";
        command.Parameters.Add(parameter);

        var fields = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) fields[reader.GetString(0)] = reader.GetString(1);
        }

        // Stored by Hangfire itself — proof the zone is what the schedule is actually read in,
        // rather than something we merely persisted on our own row.
        Assert.True(fields.TryGetValue("TimeZoneId", out var storedZone), "The job should record a zone.");
        Assert.Equal("Asia/Tokyo", storedZone);
    }
}
