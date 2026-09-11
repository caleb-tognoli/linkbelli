using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Every source type asked a schedule to go and look, so a link could not arrive until the next
/// poll and anything without a feed could not be a source at all. These cover the one that waits
/// to be pushed to.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class WebhookSourceTests(PostgresApiFactory factory)
{
    private record SourceDto(
        Guid Id, string Name, string Type, string Schedule, Guid[] PlaylistIds, string? WebhookToken,
        IReadOnlyDictionary<string, string> Config);

    private record PushDto(int Received, int Found, int Added, int Skipped, string Status, string? Error);

    private record RunDto(Guid Id, string Status, int FoundCount, int AddedCount);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<SourceDto> NewWebhookAsync(
        HttpClient client, Guid[]? playlistIds = null, object? filter = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Pushed {Guid.NewGuid():N}",
            type = "Webhook",
            config = new { },
            schedule = "",
            playlistIds = playlistIds ?? [],
            filter,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<SourceDto>())!;
    }

    /// <summary>Pushes as an outside system would: no account, no key, just the URL.</summary>
    private async Task<HttpResponseMessage> PushAsync(string token, params object[] links) =>
        await factory.CreateClient().PostAsJsonAsync($"/api/v1/hooks/{token}", new { links });

    private static string NewUrl() => $"https://pushed.example/{Guid.NewGuid():N}";

    [Fact]
    public async Task A_new_webhook_source_comes_with_its_own_token()
    {
        var client = await NewUserAsync();

        var source = await NewWebhookAsync(client);

        // Asking somebody to invent a secret is asking them to get it wrong.
        Assert.NotNull(source.WebhookToken);
        Assert.True(source.WebhookToken!.Length >= 24);
    }

    [Fact]
    public async Task A_push_lands_in_the_attached_playlist()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Pushed to");
        var source = await NewWebhookAsync(client, [playlist]);

        var res = await PushAsync(source.WebhookToken!, new { url = NewUrl(), title = "From n8n" });
        res.EnsureSuccessStatusCode();

        var push = (await res.Content.ReadFromJsonAsync<PushDto>())!;
        Assert.Equal(1, push.Received);
        Assert.Equal(1, push.Found);
        Assert.Equal("Succeeded", push.Status);

        Assert.Single(await ItemIdsAsync(playlist));
    }

    [Fact]
    public async Task A_push_needs_no_account_of_its_own()
    {
        var client = await NewUserAsync();
        var source = await NewWebhookAsync(client);

        // The URL is the whole credential: that is the point of pasting it into a Zap.
        var anonymous = factory.CreateClient();
        var res = await anonymous.PostAsJsonAsync(
            $"/api/v1/hooks/{source.WebhookToken}", new { links = new[] { new { url = NewUrl() } } });

        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_token_nobody_issued_is_refused()
    {
        var res = await PushAsync("not-a-real-token", new { url = NewUrl() });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task An_empty_push_is_refused_rather_than_recorded_as_a_run()
    {
        var client = await NewUserAsync();
        var source = await NewWebhookAsync(client);

        var res = await factory.CreateClient().PostAsJsonAsync(
            $"/api/v1/hooks/{source.WebhookToken}", new { links = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(await RunsAsync(client, source.Id));
    }

    [Fact]
    public async Task A_push_is_recorded_as_a_run_like_any_other()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "History");
        var source = await NewWebhookAsync(client, [playlist]);

        await PushAsync(source.WebhookToken!, new { url = NewUrl() }, new { url = NewUrl() });

        // Same run row, so the health page and the run history work for a pushed source too.
        var run = Assert.Single(await RunsAsync(client, source.Id));
        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(2, run.FoundCount);
    }

    [Fact]
    public async Task The_sources_filter_applies_to_a_push()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Filtered push");
        var source = await NewWebhookAsync(client, [playlist], new { titleExclude = "sponsored" });

        var res = await PushAsync(
            source.WebhookToken!,
            new { url = NewUrl(), title = "A real post" },
            new { url = NewUrl(), title = "Buy this [sponsored]" });
        res.EnsureSuccessStatusCode();

        // One road in: a pushed link gets the same filter as a polled one.
        var push = (await res.Content.ReadFromJsonAsync<PushDto>())!;
        Assert.Equal(2, push.Received);
        Assert.Equal(1, push.Found);
        Assert.Equal(1, push.Skipped);
    }

    [Fact]
    public async Task A_webhook_source_is_never_scheduled()
    {
        var client = await NewUserAsync();
        var source = await NewWebhookAsync(client);

        // A scheduled run of one would find nothing and spend a slot of the daily quota doing it.
        Assert.False(await RecurringJobExistsAsync(source.Id));
    }

    [Fact]
    public async Task The_token_is_redacted_in_the_config_and_returned_on_its_own()
    {
        var client = await NewUserAsync();
        var source = await NewWebhookAsync(client);

        // Secret like any other source secret; handed back separately because its owner has to
        // paste it somewhere, repeatedly.
        Assert.DoesNotContain(source.WebhookToken!, source.Config.Values);
        Assert.NotNull(source.WebhookToken);
    }

    private async Task<List<Guid>> ItemIdsAsync(Guid playlistId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        return await db.PlaylistItems.Where(i => i.PlaylistId == playlistId).Select(i => i.Id).ToListAsync();
    }

    private static async Task<List<RunDto>> RunsAsync(HttpClient client, Guid sourceId) =>
        (await client.GetFromJsonAsync<List<RunDto>>($"/api/v1/sources/{sourceId}/runs"))!;

    private async Task<bool> RecurringJobExistsAsync(Guid sourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Linkbelli.Infrastructure.LinkbelliDbContext>();
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
}
