using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Run history used to grow without limit, carrying a full copy of every discovered URL. These
/// cover what replaced it: real counts, a capped sample, and a prune that keeps failures longer.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SourceRunRetentionTests(PostgresApiFactory factory)
{
    private record RunDto(
        Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Status,
        string[] ItemsFound, string[] ItemsAdded, string? Error, int FoundCount, int AddedCount);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewSourceAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name,
            type = "Rss",
            config = new { feedUrl = $"https://retention.example/{Guid.NewGuid():N}.xml" },
            schedule = "0 * * * *",
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SourceDto>())!.Id;
    }

    /// <summary>Writes a run row directly — the runner needs a live fetch, which tests can't do.</summary>
    private async Task<Guid> SeedRunAsync(
        Guid sourceId, SourceRunStatus status, DateTimeOffset startedAt, int urlCount = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var urls = Enumerable.Range(0, urlCount).Select(n => $"https://retention.example/{n}").ToArray();
        var run = new SourceRun
        {
            SourceId = sourceId,
            Status = status,
            FinishedAt = startedAt,
            FoundCount = urlCount,
            AddedCount = urlCount,
            ItemsFound = urls.Take(SourceRun.SampleSize).ToArray(),
            ItemsAdded = urls.Take(SourceRun.SampleSize).ToArray(),
        };
        db.SourceRuns.Add(run);
        await db.SaveChangesAsync();

        // CreationTime is stamped on insert, so the age is set afterwards.
        run.CreationTime = startedAt;
        await db.SaveChangesAsync();

        return run.Id;
    }

    private async Task<int> PruneAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISourceRunRetention>().PruneAsync();
    }

    private async Task<bool> ExistsAsync(Guid runId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        return await db.SourceRuns.AnyAsync(r => r.Id == runId);
    }

    [Fact]
    public async Task A_run_reports_real_totals_alongside_a_capped_sample()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, "Sampled");
        await SeedRunAsync(source, SourceRunStatus.Succeeded, DateTimeOffset.UtcNow, urlCount: 100);

        var runs = await client.GetFromJsonAsync<List<RunDto>>($"/api/v1/sources/{source}/runs");
        var run = Assert.Single(runs!);

        Assert.Equal(100, run.FoundCount);
        Assert.Equal(100, run.AddedCount);
        Assert.Equal(SourceRun.SampleSize, run.ItemsFound.Length);
        Assert.Equal(SourceRun.SampleSize, run.ItemsAdded.Length);
    }

    [Fact]
    public async Task A_small_run_keeps_every_url_it_found()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, "Small run");
        await SeedRunAsync(source, SourceRunStatus.Succeeded, DateTimeOffset.UtcNow, urlCount: 3);

        var runs = await client.GetFromJsonAsync<List<RunDto>>($"/api/v1/sources/{source}/runs");
        var run = Assert.Single(runs!);

        Assert.Equal(3, run.FoundCount);
        Assert.Equal(3, run.ItemsFound.Length);
    }

    [Fact]
    public async Task Old_successful_runs_are_pruned_and_recent_ones_are_not()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, "Ageing");

        var recent = await SeedRunAsync(source, SourceRunStatus.Succeeded, DateTimeOffset.UtcNow.AddDays(-1));
        var stale = await SeedRunAsync(source, SourceRunStatus.Succeeded,
            DateTimeOffset.UtcNow.AddDays(-(ISourceRunRetention.SucceededRetentionDays + 5)));

        // Enough newer runs that the stale one is past the always-keep window.
        for (var n = 0; n < ISourceRunRetention.AlwaysKeepPerSource; n++)
        {
            await SeedRunAsync(source, SourceRunStatus.Succeeded, DateTimeOffset.UtcNow.AddHours(-n));
        }

        await PruneAsync();

        Assert.True(await ExistsAsync(recent));
        Assert.False(await ExistsAsync(stale));
    }

    [Fact]
    public async Task Failures_are_kept_longer_than_successes()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, "Failure retention");

        var age = DateTimeOffset.UtcNow.AddDays(-(ISourceRunRetention.SucceededRetentionDays + 5));
        var succeeded = await SeedRunAsync(source, SourceRunStatus.Succeeded, age);
        var failed = await SeedRunAsync(source, SourceRunStatus.Failed, age);

        for (var n = 0; n < ISourceRunRetention.AlwaysKeepPerSource; n++)
        {
            await SeedRunAsync(source, SourceRunStatus.Succeeded, DateTimeOffset.UtcNow.AddHours(-n));
        }

        await PruneAsync();

        // Same age, different outcome: the failure is the one someone comes back to diagnose.
        Assert.False(await ExistsAsync(succeeded));
        Assert.True(await ExistsAsync(failed));
    }

    [Fact]
    public async Task A_sources_most_recent_runs_survive_however_old_they_are()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, "Rarely runs");

        // Every run is well past retention, but they are all this source has.
        var ids = new List<Guid>();
        for (var n = 0; n < 3; n++)
        {
            ids.Add(await SeedRunAsync(source, SourceRunStatus.Succeeded,
                DateTimeOffset.UtcNow.AddDays(-(ISourceRunRetention.SucceededRetentionDays + 10 + n))));
        }

        await PruneAsync();

        foreach (var id in ids)
        {
            Assert.True(await ExistsAsync(id), "A source should never be left with no history at all.");
        }
    }
}
