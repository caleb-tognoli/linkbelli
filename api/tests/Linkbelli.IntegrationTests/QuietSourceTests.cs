using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Core.Entities;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The source failure nobody was told about.
/// </summary>
/// <remarks>
/// The loud one is covered: a source that errors repeatedly stops itself and says so, once. The
/// quiet one is not. A scraper whose <c>linkSelector</c> stopped matching after a site redesign,
/// or a feed that now returns an empty document, succeeds every single time and adds nothing — so
/// no status could ever reveal it, and the playlist just stops filling. You find out weeks later.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class QuietSourceTests(PostgresApiFactory factory)
{
    private record SourceDto(Guid Id, string Name, bool Quiet, bool MuteQuietAlerts);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewSourceAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name,
            type = "Rss",
            config = new Dictionary<string, string>
            {
                ["feedUrl"] = $"https://feed{Guid.NewGuid():N}.example/rss",
            },
            schedule = "0 6 * * *",
            playlistIds = Array.Empty<Guid>(),
        });
        res.EnsureSuccessStatusCode();

        return (await res.Content.ReadFromJsonAsync<SourceDto>())!.Id;
    }

    /// <summary>
    /// A run that succeeded, written straight in: making a real one happen needs a live feed,
    /// and what is being tested is what the app concludes from the record, not the fetching.
    /// </summary>
    private async Task RecordRunAsync(Guid sourceId, int added, DateTimeOffset? at = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        db.SourceRuns.Add(new SourceRun
        {
            SourceId = sourceId,
            Status = SourceRunStatus.Succeeded,
            FoundCount = added,
            AddedCount = added,
            FinishedAt = at ?? DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        if (at is { } when)
        {
            // The window is measured on CreationTime, which the interceptor stamps as now.
            await db.SourceRuns
                .Where(r => r.SourceId == sourceId)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.CreationTime, when));
        }
    }

    private static async Task<SourceDto> SourceAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<SourceDto>>("/api/v1/sources"))!.Single(s => s.Id == id);

    [Fact]
    public async Task A_source_that_runs_and_finds_nothing_is_flagged()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Broken selector {Guid.NewGuid():N}");

        await RecordRunAsync(source, added: 0);

        Assert.True((await SourceAsync(client, source)).Quiet);
    }

    [Fact]
    public async Task A_source_that_found_something_is_not()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Working {Guid.NewGuid():N}");

        await RecordRunAsync(source, added: 0);
        await RecordRunAsync(source, added: 3);

        Assert.False((await SourceAsync(client, source)).Quiet);
    }

    /// <summary>
    /// Never run is not the same as running and finding nothing. A source that has not run is
    /// new, stopped, or scheduled monthly, and none of those is the thing being looked for.
    /// </summary>
    [Fact]
    public async Task A_source_that_has_never_run_is_not_flagged()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Brand new {Guid.NewGuid():N}");

        Assert.False((await SourceAsync(client, source)).Quiet);
    }

    [Fact]
    public async Task Runs_older_than_the_window_do_not_count()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Long ago {Guid.NewGuid():N}");

        await RecordRunAsync(source, added: 0, at: DateTimeOffset.UtcNow.AddDays(-30));

        Assert.False((await SourceAsync(client, source)).Quiet);
    }

    /// <summary>
    /// Some sources are meant to be quiet — a feed that posts twice a year. Saying so once is
    /// help; saying so every week is how somebody learns to ignore the one that is broken.
    /// </summary>
    [Fact]
    public async Task It_can_be_told_that_a_source_is_meant_to_be_quiet()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Twice a year {Guid.NewGuid():N}");
        await RecordRunAsync(source, added: 0);

        Assert.True((await SourceAsync(client, source)).Quiet);

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source}", new { muteQuietAlerts = true }))
            .EnsureSuccessStatusCode();

        var muted = await SourceAsync(client, source);
        Assert.False(muted.Quiet);
        Assert.True(muted.MuteQuietAlerts);
    }

    [Fact]
    public async Task And_told_again()
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Changed my mind {Guid.NewGuid():N}");
        await RecordRunAsync(source, added: 0);

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source}", new { muteQuietAlerts = true }))
            .EnsureSuccessStatusCode();
        (await client.PatchAsJsonAsync($"/api/v1/sources/{source}", new { muteQuietAlerts = false }))
            .EnsureSuccessStatusCode();

        Assert.True((await SourceAsync(client, source)).Quiet);
    }

    /// <summary>
    /// A paused source was turned off on purpose, and one the system stopped already says so
    /// loudly. Badging either as quiet as well would be two complaints about one thing.
    /// </summary>
    [Theory]
    [InlineData("Paused")]
    [InlineData("Failing")]
    public async Task A_source_that_is_not_running_is_not_also_called_quiet(string status)
    {
        var client = await NewUserAsync();
        var source = await NewSourceAsync(client, $"Off {Guid.NewGuid():N}");
        await RecordRunAsync(source, added: 0);

        (await client.PatchAsJsonAsync($"/api/v1/sources/{source}", new { status }))
            .EnsureSuccessStatusCode();

        Assert.False((await SourceAsync(client, source)).Quiet);
    }
}
